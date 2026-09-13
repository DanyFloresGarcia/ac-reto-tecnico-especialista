using EventContracts;
using EventService.Application.Repositories;
using MassTransit;
using MediatR;
using DomainEvent = EventService.Domain.Event;

namespace EventService.Application.Events;

public class CreateEventCommandHandler : IRequestHandler<CreateEventCommand, EventDto>
{
    private readonly IEventRepository _eventRepository;
    private readonly IEventCacheService _eventCacheService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPublishEndpoint _publishEndpoint;

    public CreateEventCommandHandler(
        IEventRepository eventRepository,
        IEventCacheService eventCacheService,
        IUnitOfWork unitOfWork,
        IPublishEndpoint publishEndpoint)
    {
        _eventRepository = eventRepository;
        _eventCacheService = eventCacheService;
        _unitOfWork = unitOfWork;
        _publishEndpoint = publishEndpoint;
    }

    public async Task<EventDto> Handle(CreateEventCommand request, CancellationToken cancellationToken)
    {
        var @event = DomainEvent.Create(
            request.Name,
            request.Date,
            request.Location,
            request.Zones.Select(z => (z.Name, z.Price, z.Capacity)));

        await _eventRepository.AddAsync(@event, cancellationToken);

        // Publishing before SaveChanges: with the MassTransit EF Core Outbox (research.md §1),
        // this enqueues the message into the same DbContext change tracker, so it commits atomically
        // with Event/Zone in the single SaveChangesAsync below (FR-008 — never one without the other).
        await _publishEndpoint.Publish(new EventCreated
        {
            MessageId = Guid.NewGuid(),
            EventId = @event.Id,
            Name = @event.Name,
            OccurredAt = DateTime.UtcNow,
            CorrelationId = request.CorrelationId,
            Version = 1,
        }, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // events:list would now be stale for anyone reading between the write and the next
        // natural expiry (research.md §3) — invalidate it explicitly right after the commit.
        await _eventCacheService.InvalidateEventListAsync(cancellationToken);

        return EventMapper.ToDto(@event);
    }
}
