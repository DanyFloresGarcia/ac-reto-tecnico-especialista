using NotificationService.Domain;

namespace NotificationService.Application.Repositories;

public interface IAuditLogRepository
{
    Task<AuditLog?> GetByMessageIdAsync(Guid messageId, CancellationToken cancellationToken);

    /// <summary>
    /// Attempts to insert the row. Returns false (without throwing) if a row for the same
    /// MessageId already exists — this is the real idempotency guarantee (a DB unique
    /// constraint), not just the caller's prior GetByMessageIdAsync check, which has a race
    /// window under concurrent redelivery (data-model.md §AuditLog).
    /// </summary>
    Task<bool> TryAddAsync(AuditLog auditLog, CancellationToken cancellationToken);
}
