using EventService.Application.Events;

namespace EventService.Application.Repositories;

// Cache-Aside over Redis (research.md §3): keys events:list / events:{id}, TTL 60s.
public interface IEventCacheService
{
    Task<IReadOnlyList<EventDto>?> GetEventListAsync(CancellationToken cancellationToken);
    Task SetEventListAsync(IReadOnlyList<EventDto> events, CancellationToken cancellationToken);
    Task InvalidateEventListAsync(CancellationToken cancellationToken);

    Task<EventDto?> GetEventAsync(Guid id, CancellationToken cancellationToken);
    Task SetEventAsync(EventDto @event, CancellationToken cancellationToken);
}
