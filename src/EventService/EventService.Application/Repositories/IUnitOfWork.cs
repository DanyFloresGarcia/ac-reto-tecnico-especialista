namespace EventService.Application.Repositories;

// Implemented directly by EventDbContext (Infrastructure) — its SaveChangesAsync already
// persists Event+Zones+OutboxMessage atomically (Constitucion §7.5 Unit of Work pattern).
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
