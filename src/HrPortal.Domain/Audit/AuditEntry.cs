namespace HrPortal.Domain.Audit;

/// <summary>Append-only audit record. Rows are never updated or deleted.</summary>
public sealed class AuditEntry
{
    private AuditEntry()
    {
    }

    public long Id { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    public string ActorId { get; private set; } = null!;

    public string ActorName { get; private set; } = null!;

    public AuditAction Action { get; private set; }

    public string EntityType { get; private set; } = null!;

    public string EntityId { get; private set; } = null!;

    /// <summary>JSON object of property values before the change; sensitive values are masked.</summary>
    public string? OldValues { get; private set; }

    /// <summary>JSON object of property values after the change; sensitive values are masked.</summary>
    public string? NewValues { get; private set; }

    public static AuditEntry Create(
        DateTimeOffset occurredAt,
        string actorId,
        string actorName,
        AuditAction action,
        string entityType,
        string entityId,
        string? oldValues = null,
        string? newValues = null) => new()
        {
            OccurredAtUtc = occurredAt.ToUniversalTime(),
            ActorId = actorId,
            ActorName = actorName,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            OldValues = oldValues,
            NewValues = newValues,
        };
}
