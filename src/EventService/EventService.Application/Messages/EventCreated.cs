// Deliberately NOT namespaced under EventService.* (Constitucion §4/§7.3: no shared assembly
// between services). MassTransit identifies a message's wire identity by
// "urn:message:{Namespace}:{TypeName}" — NOT by exchange name alone — so NotificationService's
// independently-compiled copy of this type must live under this exact same namespace string
// for the two sides to recognize each other's messages as the same contract. Only this file's
// namespace is shared "by convention"; there is no project/assembly reference between the two
// services.
namespace EventContracts;

/// <summary>
/// The single integration contract between EventService and NotificationService, sent via
/// RabbitMQ through the transactional Outbox. Schema is fixed — see
/// contracts/event-created-message.md; `version` stays 1 for this phase.
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
