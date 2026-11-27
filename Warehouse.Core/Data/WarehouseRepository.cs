using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Warehouse.Core.Models;
using Warehouse.Core.Services;

namespace Warehouse.Core.Data;

public sealed partial class WarehouseRepository
{
    private readonly string _connectionString;
    private readonly Guid _streamId;
    private readonly string _auditPath;
    private AuditFileStatus _auditStatus;

    // По умолчанию у каждого профиля Windows и подключения к базе свой файл. Два клиента
    // не принимают отсутствие файлов друг друга за фальсификацию; эталон обоих находится в SQL.
    // Необязательные параметры также позволяют изолировать интеграционные тесты.
    public WarehouseRepository(string connectionString, string? auditDirectory = null, Guid? auditStreamId = null)
    {
        _connectionString = connectionString;
        var builder = new SqlConnectionStringBuilder(connectionString);
        var key = $"{Environment.MachineName}|{Environment.UserDomainName}|{Environment.UserName}|{builder.DataSource}|{builder.InitialCatalog}".ToUpperInvariant();
        _streamId = auditStreamId ?? new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(key)).AsSpan(0, 16));
        var directory = auditDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WarehouseCourse", "Audit");
        _auditPath = Path.Combine(directory, $"audit-{_streamId:N}.jsonl");
        _auditStatus = new AuditFileStatus(AuditFileState.NotChecked, "Журнал ещё не проверен.", _auditPath);
    }

    // Разные базы не должны перезаписывать друг другу одинаковые номера файлов ТТН.
    public string InvoiceStorageKey
    {
        get
        {
            var builder=new SqlConnectionStringBuilder(_connectionString);
            var source=$"{builder.DataSource}|{builder.InitialCatalog}".ToUpperInvariant();
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..16];
        }
    }

    public Guid AuditStreamId => _streamId;
    public string AuditFilePath => _auditPath;
    public AuditFileStatus AuditStatus => _auditStatus;
    public event EventHandler<AuditFileStatus>? AuditStatusChanged;

    private SqlConnection Open()
    {
        var connection = new SqlConnection(_connectionString);
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }

    private static void Add(SqlCommand cmd, string name, SqlDbType type, object? value, int size = 0)
    {
        var p = size != 0 ? cmd.Parameters.Add(name, type, size) : cmd.Parameters.Add(name, type);
        if (type == SqlDbType.Decimal) { p.Precision = 18; p.Scale = 3; }
        p.Value = value ?? DBNull.Value;
    }

    private static void AddDecimal(SqlCommand cmd, string name, decimal? value, byte scale)
    {
        var p=cmd.Parameters.Add(name,SqlDbType.Decimal); p.Precision=18; p.Scale=scale; p.Value=(object?)value ?? DBNull.Value;
    }

    private static string Pattern(string? text)
    {
        var value = Validation.Optional(text, "Поиск", 200);
        return "%" + value.Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[") + "%";
    }

    // Роль берём из базы при каждом изменении, а не доверяем значению, пришедшему из интерфейса.
    private static AppUser RequireActor(SqlConnection c, SqlTransaction tx, AppUser actor, Func<AppUser, bool> allowed)
    {
        if (actor.Id <= 0) throw new UnauthorizedAccessException("Сначала войдите в программу.");
        using var q = new SqlCommand("SELECT Id,Login,FullName,Role FROM dbo.WarehouseUsers WITH (HOLDLOCK) WHERE Id=@id AND IsActive=1", c, tx);
        Add(q, "@id", SqlDbType.Int, actor.Id);
        using var r = q.ExecuteReader();
        if (!r.Read()) throw new UnauthorizedAccessException("Учётная запись удалена или недоступна. Войдите заново.");
        var actual = new AppUser(r.GetInt32(0), r.GetString(1), r.GetString(2), (UserRole)r.GetByte(3));
        if (!allowed(actual)) throw new UnauthorizedAccessException("У вашей роли нет прав на это действие.");
        return actual;
    }

    public AppUser? Authenticate(string login, string password)
    {
        // Даже неуспешный вход фиксируется. Пароли никогда не попадают в журнал.
        var attemptedLogin = (login ?? "").Trim();
        var loginTooLong = attemptedLogin.Length > 80;
        if (loginTooLong) attemptedLogin = attemptedLogin[..80];
        using var connection = Open();
        using var tx = connection.BeginTransaction();
        AppUser? user = null;
        if (!loginTooLong && attemptedLogin.Length > 0 && !string.IsNullOrEmpty(password))
        {
            using var command = new SqlCommand(@"SELECT Id,Login,FullName,Role,PasswordSalt,PasswordHash,PasswordIterations
                FROM dbo.WarehouseUsers WHERE Login=@login AND IsActive=1", connection, tx);
            Add(command, "@login", SqlDbType.NVarChar, attemptedLogin, 80);
            using var r = command.ExecuteReader();
            if (r.Read() && PasswordHasher.Verify(password, (byte[])r[4], (byte[])r[5], r.GetInt32(6)))
                user = new AppUser(r.GetInt32(0), r.GetString(1), r.GetString(2), (UserRole)r.GetByte(3));
        }
        WriteAudit(connection, tx, user, user is null ? "Неуспешный вход" : "Вход в программу", "Учётная запись", user?.Id.ToString() ?? "", new { }, attemptedLogin);
        tx.Commit();
        SyncAuditFile();
        return user;
    }

    public void ChangePassword(AppUser user, string current, string replacement)
    {
        Validation.Password(replacement);
        using var c = Open();
        using var tx = c.BeginTransaction(IsolationLevel.Serializable);
        var actual = RequireActor(c, tx, user, _ => true);
        using (var check = new SqlCommand("SELECT PasswordSalt,PasswordHash,PasswordIterations FROM dbo.WarehouseUsers WITH (UPDLOCK,HOLDLOCK) WHERE Id=@id AND IsActive=1", c, tx))
        {
            Add(check, "@id", SqlDbType.Int, actual.Id);
            using var r = check.ExecuteReader();
            if (!r.Read() || !PasswordHasher.Verify(current, (byte[])r[0], (byte[])r[1], r.GetInt32(2)))
                throw new ArgumentException("Текущий пароль неверен.");
        }
        var (salt, hash) = PasswordHasher.Hash(replacement);
        using (var q = new SqlCommand("UPDATE dbo.WarehouseUsers SET PasswordSalt=@salt,PasswordHash=@hash,PasswordIterations=@iterations WHERE Id=@id", c, tx))
        {
            Add(q, "@salt", SqlDbType.VarBinary, salt, 16); Add(q, "@hash", SqlDbType.VarBinary, hash, 32);
            Add(q, "@iterations", SqlDbType.Int, PasswordHasher.Iterations); Add(q, "@id", SqlDbType.Int, actual.Id);
            if (q.ExecuteNonQuery() != 1) throw new InvalidOperationException("Пользователь больше не существует.");
        }
        WriteAudit(c, tx, actual, "Изменён собственный пароль", "Учётная запись", actual.Id.ToString(), new { Login = actual.Login });
        tx.Commit();
        SyncAuditFile();
    }

    public DateTime GetServerTime()
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT SYSDATETIME();";
        return (DateTime)command.ExecuteScalar()!;
    }

    public LegacyDataIssues GetLegacyDataIssues()
    {
        using var c = Open(); using var q = c.CreateCommand();
        // Совпадает с SQL-проверкой и .NET Trim: старые поля из табуляций и других
        // пробельных Unicode-символов тоже показываются как требующие исправления.
        const string whitespace = "NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288)";
        static string HasText(string column) => $"LEN(LTRIM(RTRIM(TRANSLATE({column},{whitespace},REPLICATE(N' ',24)))))>0";
        q.CommandText = $@"SELECT (SELECT COUNT(*) FROM dbo.Products WHERE PurchasePrice<=0 OR SalePrice<=0),
            (SELECT COUNT(*) FROM dbo.Warehouses WHERE NOT ({HasText("Name")} AND {HasText("Address")} AND
                {HasText("ManagerName")} AND {HasText("ManagerContact")}))";
        using var r = q.ExecuteReader(); r.Read();
        return new LegacyDataIssues(r.GetInt32(0), r.GetInt32(1));
    }
}
