namespace NotificationService.Domain;

public enum AuditLogStatus
{
    Processed = 0,
    Failed = 1,
}

/// <summary>
/// Simple entity (no rich DDD/Aggregate treatment — Constitucion §7.5): a plain audit record
/// with no business behavior of its own. Uniqueness of MessageId is enforced at the database
/// level (see AuditLogEntityConfiguration), not just by application-level checks — that's the
/// real idempotency guarantee (data-model.md §AuditLog).
/// </summary>
public class AuditLog : Entity
{
    public Guid MessageId { get; private set; }
    public Guid EventId { get; private set; }
    public string EventName { get; private set; }
    public DateTime OccurredAt { get; private set; }
    public Guid CorrelationId { get; private set; }
    public string PayloadHash { get; private set; }
    public DateTime ProcessedAt { get; private set; }
    public AuditLogStatus Status { get; private set; }

    private AuditLog(
        Guid id,
        Guid messageId,
        Guid eventId,
        string eventName,
        DateTime occurredAt,
        Guid correlationId,
        string payloadHash,
        AuditLogStatus status) : base(id)
    {
        MessageId = messageId;
        EventId = eventId;
        EventName = eventName;
        OccurredAt = occurredAt;
        CorrelationId = correlationId;
        PayloadHash = payloadHash;
        ProcessedAt = DateTime.UtcNow;
        Status = status;
    }

    public static AuditLog CreateProcessed(
        Guid messageId, Guid eventId, string eventName, DateTime occurredAt, Guid correlationId, string payloadHash) =>
        new(Guid.NewGuid(), messageId, eventId, eventName, occurredAt, correlationId, payloadHash, AuditLogStatus.Processed);

    public static AuditLog CreateFailed(
        Guid messageId, Guid eventId, string eventName, DateTime occurredAt, Guid correlationId, string payloadHash) =>
        new(Guid.NewGuid(), messageId, eventId, eventName, occurredAt, correlationId, payloadHash, AuditLogStatus.Failed);

#pragma warning disable CS8618 // required by EF Core materialization
    private AuditLog()
    {
    }
#pragma warning restore CS8618
}
