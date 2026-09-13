namespace NotificationService.Application.Notifications;

// Only fields actually present on the EventCreated message contract are available here
// (contracts/event-created-message.md has no `location`) — the simulated email uses
// eventId/name/occurredAt, not the fuller "nombre/fecha/lugar" language from the original
// brief, since the wire schema is frozen at version 1 and adding a field would be a breaking change.
public interface IEmailSender
{
    Task SendEventCreatedNotificationAsync(
        Guid eventId, string eventName, DateTime occurredAt, CancellationToken cancellationToken);
}
