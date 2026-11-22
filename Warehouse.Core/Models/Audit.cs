namespace Warehouse.Core.Models;

public enum AuditFileState { NotChecked, Verified, Diverged, Unavailable }

/// <summary>Результат сверки только локального файла с эталоном этого потока в SQL.</summary>
public sealed record AuditFileStatus(AuditFileState State, string Message, string Path, int VerifiedEntries = 0);

public sealed record AuditEntry(
    long Id, Guid StreamId, DateTime OccurredAtUtc, int? ActorId, string ActorLogin,
    string ActorName, string ActorRole, string Action, string Entity, string EntityId, string Details)
{
    public DateTime LocalTime => DateTime.SpecifyKind(OccurredAtUtc, DateTimeKind.Utc).ToLocalTime();
}

public sealed record LegacyDataIssues(int Products, int Warehouses)
{
    public bool HasIssues => Products > 0 || Warehouses > 0;
}
