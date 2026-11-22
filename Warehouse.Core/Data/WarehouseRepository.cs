using System.Data;
using Microsoft.Data.SqlClient;
using Warehouse.Core.Models;
using Warehouse.Core.Services;

namespace Warehouse.Core.Data;

public sealed partial class WarehouseRepository(string connectionString)
{
    private SqlConnection Open()
    {
        var connection = new SqlConnection(connectionString);
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }

    private static void Add(SqlCommand cmd, string name, SqlDbType type, object? value, int size = 0)
    {
        var p = size > 0 ? cmd.Parameters.Add(name, type, size) : cmd.Parameters.Add(name, type);
        if (type == SqlDbType.Decimal) { p.Precision = 18; p.Scale = 3; }
        p.Value = value ?? DBNull.Value;
    }

    private static string Pattern(string? text)
    {
        var value = Validation.Optional(text, "Поиск", 200);
        return "%" + value.Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[") + "%";
    }

    public AppUser? Authenticate(string login, string password)
    {
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrEmpty(password)) return null;
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT Id, Login, FullName, Role, PasswordSalt, PasswordHash, PasswordIterations
                                FROM dbo.WarehouseUsers WHERE Login = @login";
        Add(command, "@login", SqlDbType.NVarChar, login.Trim(), 80);
        using var r = command.ExecuteReader();
        if (!r.Read()) return null;
        if (!PasswordHasher.Verify(password, (byte[])r[4], (byte[])r[5], r.GetInt32(6))) return null;
        return new AppUser(r.GetInt32(0), r.GetString(1), r.GetString(2), (UserRole)r.GetByte(3));
    }

    public void ChangePassword(AppUser user, string current, string replacement)
    {
        if (replacement.Length < 8 || replacement.Length > 200 || string.IsNullOrWhiteSpace(replacement))
            throw new ArgumentException("Новый пароль должен содержать от 8 до 200 символов.");
        if (Authenticate(user.Login, current)?.Id != user.Id) throw new ArgumentException("Текущий пароль неверен.");
        var (salt, hash) = PasswordHasher.Hash(replacement);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"UPDATE dbo.WarehouseUsers SET PasswordSalt=@salt, PasswordHash=@hash, PasswordIterations=@iterations WHERE Id=@id";
        Add(command, "@salt", SqlDbType.VarBinary, salt, 16);
        Add(command, "@hash", SqlDbType.VarBinary, hash, 32);
        Add(command, "@iterations", SqlDbType.Int, PasswordHasher.Iterations);
        Add(command, "@id", SqlDbType.Int, user.Id);
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("Пользователь больше не существует.");
    }

    public DateTime GetServerTime()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT SYSDATETIME();";
        return (DateTime)command.ExecuteScalar()!;
    }
}
