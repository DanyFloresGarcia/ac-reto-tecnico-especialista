namespace EventService.Application.Events;

// Response shape shared by POST /events, GET /events and GET /events/{id} (contracts/events-api.md).
public record EventDto(Guid Id, string Name, DateTime Date, string Location, string Status, IReadOnlyList<ZoneDto> Zones);

public record ZoneDto(Guid Id, string Name, decimal Price, int Capacity);
