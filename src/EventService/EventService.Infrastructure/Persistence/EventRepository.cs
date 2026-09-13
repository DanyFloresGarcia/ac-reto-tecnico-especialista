using EventService.Application.Repositories;
using EventService.Domain;
using Microsoft.EntityFrameworkCore;

namespace EventService.Infrastructure.Persistence;

public class EventRepository : IEventRepository
{
    private readonly EventDbContext _dbContext;

    public EventRepository(EventDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(Event @event, CancellationToken cancellationToken)
    {
        await _dbContext.Events.AddAsync(@event, cancellationToken);
    }

    public Task<Event?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _dbContext.Events
            .Include(e => e.Zones)
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Event>> GetAllAsync(CancellationToken cancellationToken) =>
        await _dbContext.Events
            .Include(e => e.Zones)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    public Task<bool> AnyAsync(CancellationToken cancellationToken) =>
        _dbContext.Events.AnyAsync(cancellationToken);
}
