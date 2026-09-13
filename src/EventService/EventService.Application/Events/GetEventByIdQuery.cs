using EventService.Application.Repositories;
using MediatR;

namespace EventService.Application.Events;

public record GetEventByIdQuery(Guid Id) : IRequest<EventDto?>;

public class GetEventByIdQueryHandler : IRequestHandler<GetEventByIdQuery, EventDto?>
{
    private readonly IEventRepository _eventRepository;
    private readonly IEventCacheService _eventCacheService;

    public GetEventByIdQueryHandler(IEventRepository eventRepository, IEventCacheService eventCacheService)
    {
        _eventRepository = eventRepository;
        _eventCacheService = eventCacheService;
    }

    public async Task<EventDto?> Handle(GetEventByIdQuery request, CancellationToken cancellationToken)
    {
        var cached = await _eventCacheService.GetEventAsync(request.Id, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var @event = await _eventRepository.GetByIdAsync(request.Id, cancellationToken);
        if (@event is null)
        {
            return null;
        }

        var dto = EventMapper.ToDto(@event);
        await _eventCacheService.SetEventAsync(dto, cancellationToken);

        return dto;
    }
}
