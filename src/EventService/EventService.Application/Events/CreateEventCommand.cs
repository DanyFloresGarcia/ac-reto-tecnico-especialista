using MediatR;

namespace EventService.Application.Events;

public record CreateEventZoneInput(string Name, decimal Price, int Capacity);

public record CreateEventCommand(
    string Name,
    DateTime Date,
    string Location,
    IReadOnlyList<CreateEventZoneInput> Zones,
    Guid CorrelationId) : IRequest<EventDto>;
