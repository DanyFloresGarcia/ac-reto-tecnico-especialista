using EventService.Domain;

namespace EventService.Application.Repositories;

public interface IEventRepository
{
    Task AddAsync(Event @event, CancellationToken cancellationToken);
    Task<Event?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Event>> GetAllAsync(CancellationToken cancellationToken);
    Task<bool> AnyAsync(CancellationToken cancellationToken);
}
