using EventService.Application.Repositories;
using MediatR;

namespace EventService.Application.Events;

public record GetEventsQuery : IRequest<IReadOnlyList<EventDto>>;

public class GetEventsQueryHandler : IRequestHandler<GetEventsQuery, IReadOnlyList<EventDto>>
{
    private readonly IEventRepository _eventRepository;
    private readonly IEventCacheService _eventCacheService;

    public GetEventsQueryHandler(IEventRepository eventRepository, IEventCacheService eventCacheService)
    {
        _eventRepository = eventRepository;
        _eventCacheService = eventCacheService;
    }

    public async Task<IReadOnlyList<EventDto>> Handle(GetEventsQuery request, CancellationToken cancellationToken)
    {
        var cached = await _eventCacheService.GetEventListAsync(cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var events = await _eventRepository.GetAllAsync(cancellationToken);
        var dtos = events.Select(EventMapper.ToDto).ToList();

        await _eventCacheService.SetEventListAsync(dtos, cancellationToken);

        return dtos;
    }
}
