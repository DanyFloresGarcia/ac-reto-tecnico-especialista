using Microsoft.EntityFrameworkCore;
using NotificationService.Application.Repositories;
using NotificationService.Domain;
using Npgsql;

namespace NotificationService.Infrastructure.Persistence;

public class AuditLogRepository : IAuditLogRepository
{
    private const string PostgresUniqueViolationSqlState = "23505";

    private readonly NotificationDbContext _dbContext;

    public AuditLogRepository(NotificationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<AuditLog?> GetByMessageIdAsync(Guid messageId, CancellationToken cancellationToken) =>
        _dbContext.AuditLogs.AsNoTracking().FirstOrDefaultAsync(a => a.MessageId == messageId, cancellationToken);

    public async Task<bool> TryAddAsync(AuditLog auditLog, CancellationToken cancellationToken)
    {
        await _dbContext.AuditLogs.AddAsync(auditLog, cancellationToken);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresUniqueViolationSqlState })
        {
            // Two deliveries of the same MessageId raced past the fast-path ExistsAsync check;
            // the unique index is what actually enforces idempotency here (data-model.md §AuditLog).
            _dbContext.Entry(auditLog).State = EntityState.Detached;
            return false;
        }
    }
}
