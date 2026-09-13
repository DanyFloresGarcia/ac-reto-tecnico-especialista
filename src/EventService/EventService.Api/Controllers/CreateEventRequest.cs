namespace EventService.Api.Controllers;

// HTTP-facing shape for POST /events (contracts/events-api.md) — kept separate from
// CreateEventCommand so the Api layer, not Application, owns the wire format.
public record CreateEventRequest(string Name, DateTime Date, string Location, List<CreateEventZoneRequest> Zones);

public record CreateEventZoneRequest(string Name, decimal Price, int Capacity);
