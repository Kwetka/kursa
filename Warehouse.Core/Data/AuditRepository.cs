using System.Data;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Warehouse.Core.Models;

namespace Warehouse.Core.Data;

public sealed partial class WarehouseRepository
{
    private static readonly JsonSerializerOptions AuditJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private void LockAuditStream(SqlConnection c, SqlTransaction tx)
    {
        using var q = new SqlCommand(@"DECLARE @result int;
            EXEC @result=sys.sp_getapplock @Resource=@resource,@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=15000;
            IF @result<0 THROW 51010, N'Не удалось заблокировать журнал аудита.', 1;
            IF NOT EXISTS (SELECT 1 FROM dbo.AuditStreams WITH (UPDLOCK,HOLDLOCK) WHERE StreamId=@stream)
                INSERT dbo.AuditStreams(StreamId) VALUES (@stream);", c, tx);
        Add(q, "@resource", SqlDbType.NVarChar, "WarehouseCourse.Audit:" + _streamId.ToString("N"), 255);
        Add(q, "@stream", SqlDbType.UniqueIdentifier, _streamId);
        q.ExecuteNonQuery();
    }

    // Вызывается внутри той же SQL-транзакции, что и изменение данных: при откате
    // обе записи исчезают, при фиксации эталон всегда содержит событие.
    private void WriteAudit(SqlConnection c, SqlTransaction tx, AppUser? actor, string action,
        string entity, string entityId, object details, string? attemptedLogin = null)
    {
        LockAuditStream(c, tx);
        using var q = new SqlCommand(@"INSERT dbo.AuditEvents(StreamId,ActorId,ActorLogin,ActorName,ActorRole,Action,Entity,EntityId,Details)
            VALUES (@stream,@actorId,@login,@name,@role,@action,@entity,@entityId,@details)", c, tx);
        Add(q, "@stream", SqlDbType.UniqueIdentifier, _streamId);
        Add(q, "@actorId", SqlDbType.Int, actor?.Id);
        Add(q, "@login", SqlDbType.NVarChar, actor?.Login ?? attemptedLogin ?? "", 80);
        Add(q, "@name", SqlDbType.NVarChar, actor?.FullName ?? "—", 200);
        Add(q, "@role", SqlDbType.NVarChar, actor?.RoleName ?? "Не определена", 80);
        Add(q, "@action", SqlDbType.NVarChar, action, 120); Add(q, "@entity", SqlDbType.NVarChar, entity, 80);
        Add(q, "@entityId", SqlDbType.NVarChar, entityId, 80);
        Add(q, "@details", SqlDbType.NVarChar, JsonSerializer.Serialize(details, AuditJson), -1);
        q.ExecuteNonQuery();
    }

    private static AuditEntry ReadAuditEntry(SqlDataReader r) => new(
        r.GetInt64(0), r.GetGuid(1), DateTime.SpecifyKind(r.GetDateTime(2), DateTimeKind.Utc),
        r.IsDBNull(3) ? null : r.GetInt32(3), r.GetString(4), r.GetString(5), r.GetString(6),
        r.GetString(7), r.GetString(8), r.GetString(9), r.GetString(10));

    private const string AuditColumns = "Id,StreamId,OccurredAtUtc,ActorId,ActorLogin,ActorName,ActorRole,Action,Entity,EntityId,Details";

    /// <summary>Последние события всех клиентов, сохранённые в SQL; файл текущего клиента проверяется отдельно.</summary>
    public List<AuditEntry> GetAuditEntries(int limit = 1000)
    {
        if (limit is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(limit));
        var result = new List<AuditEntry>();
        using var c = Open(); using var q = c.CreateCommand();
        q.CommandText = $"SELECT TOP (@limit) {AuditColumns} FROM dbo.AuditEvents ORDER BY Id DESC";
        Add(q, "@limit", SqlDbType.Int, limit);
        using var r = q.ExecuteReader();
        while (r.Read()) result.Add(ReadAuditEntry(r));
        return result;
    }

    // Используем только явно перечисленные поля: вычисляемые свойства для интерфейса
    // не должны влиять на формат файла и его сравнение между машинами.
    private static byte[] AuditLine(AuditEntry e) => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
    {
        e.Id, e.StreamId, e.OccurredAtUtc, e.ActorId, e.ActorLogin, e.ActorName,
        e.ActorRole, e.Action, e.Entity, e.EntityId, e.Details
    }, AuditJson) + "\n");

    /// <summary>
    /// Сначала сравнивает каждый байт с событиями SQL. Дописывает лишь события,
    /// ещё не подтверждённые в контрольной точке SQL; обнаруженное расхождение не исправляет.
    /// Данные операции уже сохранены, поэтому ошибка файлового журнала возвращается как статус,
    /// а не как исключение, которое могло бы побудить пользователя повторно провести операцию.
    /// </summary>
    public AuditFileStatus SyncAuditFile()
    {
        AuditFileStatus status;
        try
        {
            using var c = Open();
            using var tx = c.BeginTransaction(IsolationLevel.Serializable);
            LockAuditStream(c, tx);
            long checkpoint;
            using (var q = new SqlCommand("SELECT LastWrittenEventId FROM dbo.AuditStreams WHERE StreamId=@stream", c, tx))
            {
                Add(q, "@stream", SqlDbType.UniqueIdentifier, _streamId);
                checkpoint = Convert.ToInt64(q.ExecuteScalar());
            }
            var entries = new List<AuditEntry>();
            using (var q = new SqlCommand($"SELECT {AuditColumns} FROM dbo.AuditEvents WHERE StreamId=@stream ORDER BY Id", c, tx))
            {
                Add(q, "@stream", SqlDbType.UniqueIdentifier, _streamId);
                using var r = q.ExecuteReader();
                while (r.Read()) entries.Add(ReadAuditEntry(r));
            }
            if (checkpoint > 0 && !entries.Any(e => e.Id == checkpoint))
                throw new AuditDivergenceException("Контрольная точка отсутствует в SQL-эталоне.");
            if (!File.Exists(_auditPath) && checkpoint > 0)
                throw new AuditDivergenceException("Ранее записанный файл не найден или недоступен.");

            Directory.CreateDirectory(Path.GetDirectoryName(_auditPath)!);
            using var file = new FileStream(_auditPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
            var matched = 0;
            long lastMatchedId = 0;
            foreach (var entry in entries)
            {
                var expected = AuditLine(entry);
                var remaining = file.Length - file.Position;
                if (remaining == 0) break;
                if (remaining < expected.Length) throw new AuditDivergenceException("Файл оборван внутри записи.");
                var actual = new byte[expected.Length];
                file.ReadExactly(actual);
                if (!actual.AsSpan().SequenceEqual(expected)) throw new AuditDivergenceException($"Запись SQL №{entry.Id} не совпадает с файлом.");
                matched++; lastMatchedId = entry.Id;
            }
            if (file.Position != file.Length) throw new AuditDivergenceException("В файле есть лишние записи или байты.");
            if (checkpoint > lastMatchedId) throw new AuditDivergenceException("Файл усечён относительно сохранённой контрольной точки.");

            // Отсутствующие после контрольной точки события ещё не были подтверждены в файле:
            // например, приложение завершилось после коммита SQL и до записи файла.
            foreach (var entry in entries.Skip(matched)) file.Write(AuditLine(entry));
            file.Flush(flushToDisk: true);
            var lastId = entries.Count == 0 ? 0 : entries[^1].Id;
            using (var q = new SqlCommand("UPDATE dbo.AuditStreams SET LastWrittenEventId=@last WHERE StreamId=@stream", c, tx))
            {
                Add(q, "@last", SqlDbType.BigInt, lastId); Add(q, "@stream", SqlDbType.UniqueIdentifier, _streamId);
                q.ExecuteNonQuery();
            }
            // Освобождаем файл до освобождения SQL-блокировки: другой экземпляр не должен
            // увидеть уже отпущенный SQL-lock, пока файл ещё открыт на запись.
            file.Dispose();
            tx.Commit();
            status = new AuditFileStatus(AuditFileState.Verified, $"Файл совпадает с SQL-эталоном ({entries.Count} записей).", _auditPath, entries.Count);
        }
        catch (AuditDivergenceException ex)
        {
            status = new AuditFileStatus(AuditFileState.Diverged,
                "РАСХОЖДЕНИЕ: файл журнала не совпадает с SQL-эталоном. " + ex.Message +
                " Причина и автор неизвестны; автоматическое исправление отключено.", _auditPath);
        }
        catch (Exception ex)
        {
            status = new AuditFileStatus(AuditFileState.Unavailable,
                "Не удалось записать или проверить файл журнала. Изменения, уже сохранённые в SQL, не отменены; записи ожидают проверки. " + ex.Message, _auditPath);
        }
        _auditStatus = status;
        // Обработчик интерфейса не должен превращать уже сохранённую операцию в кажущуюся ошибку.
        try { AuditStatusChanged?.Invoke(this, status); } catch { /* status остаётся доступным для повторной проверки */ }
        return status;
    }

    private sealed class AuditDivergenceException(string message) : Exception(message);
}
