// See EventService.Application/Messages/EventCreated.cs for why this namespace is shared by
// convention (not by a project/assembly reference) between the two independently-compiled
// services — it's how MassTransit's message-type URN ends up matching on both sides.
namespace EventContracts;

/// <summary>
/// NotificationService's own copy of the integration contract — see
/// contracts/event-created-message.md. Intentionally NOT shared via a project reference to
/// EventService (Constitucion §4/§7.3: services are coupled only by this message shape on the
/// bus, never by code); the two types match structurally, which is all MassTransit needs.
/// </summary>
public record EventCreated
{
    public Guid MessageId { get; init; }
    public Guid EventId { get; init; }
    public string Name { get; init; } = string.Empty;
    public DateTime OccurredAt { get; init; }
    public Guid CorrelationId { get; init; }
    public int Version { get; init; } = 1;
}
