namespace EventService.Domain;

/// <summary>
/// Internal domain event raised by Event.Create — not to be confused with the RabbitMQ
/// integration message `EventCreated` (see contracts/event-created-message.md), which
/// EventService.Application builds from this and publishes via the transactional Outbox.
/// </summary>
public sealed class EventCreatedDomainEvent
{
    public Guid EventId { get; }
    public string Name { get; }
    public DateTime OccurredAt { get; }

    public EventCreatedDomainEvent(Guid eventId, string name, DateTime occurredAt)
    {
        EventId = eventId;
        Name = name;
        OccurredAt = occurredAt;
    }
}
