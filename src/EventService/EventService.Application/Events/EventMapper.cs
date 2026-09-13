using EventService.Domain;

namespace EventService.Application.Events;

public static class EventMapper
{
    public static EventDto ToDto(Event @event) => new(
        @event.Id,
        @event.Name,
        @event.Date,
        @event.Location,
        @event.Status.ToString(),
        @event.Zones.Select(z => new ZoneDto(z.Id, z.Name, z.Price, z.Capacity)).ToList());
}
