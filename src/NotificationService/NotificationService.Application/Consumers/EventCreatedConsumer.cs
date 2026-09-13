using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.Logging;
using EventContracts;
using NotificationService.Application.Notifications;
using NotificationService.Application.Repositories;
using NotificationService.Domain;

namespace NotificationService.Application.Consumers;

/// <summary>
/// Idempotent consumer for the EventCreated integration message (contracts/event-created-message.md).
/// See data-model.md §AuditLog for the exact guarantee this implements.
/// </summary>
public class EventCreatedConsumer : IConsumer<EventCreated>
{
    private readonly IAuditLogRepository _repository;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<EventCreatedConsumer> _logger;

    public EventCreatedConsumer(IAuditLogRepository repository, IEmailSender emailSender, ILogger<EventCreatedConsumer> logger)
    {
        _repository = repository;
        _emailSender = emailSender;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<EventCreated> context)
    {
        var message = context.Message;
        var payloadHash = ComputePayloadHash(message);

        // Fast path: avoids the email-send + insert attempt in the common "already processed" case.
        var existing = await _repository.GetByMessageIdAsync(message.MessageId, context.CancellationToken);
        if (existing is not null)
        {
            if (existing.PayloadHash != payloadHash)
            {
                _logger.LogWarning(
                    "MessageId {MessageId} ya fue procesado pero con un payload distinto — se conserva el original, no se sobrescribe.",
                    message.MessageId);
            }
            else
            {
                _logger.LogInformation("Mensaje duplicado ignorado. MessageId: {MessageId}", message.MessageId);
            }

            return;
        }

        await _emailSender.SendEventCreatedNotificationAsync(
            message.EventId, message.Name, message.OccurredAt, context.CancellationToken);

        var auditLog = AuditLog.CreateProcessed(
            message.MessageId, message.EventId, message.Name, message.OccurredAt, message.CorrelationId, payloadHash);

        // The real idempotency guarantee: if two deliveries raced past the check above, only
        // one INSERT wins the unique constraint on MessageId — the other is treated as a
        // duplicate here, never as a processing failure that would trigger a retry/DLQ.
        var inserted = await _repository.TryAddAsync(auditLog, context.CancellationToken);
        if (!inserted)
        {
            _logger.LogInformation(
                "Mensaje duplicado detectado al insertar (entrega concurrente). MessageId: {MessageId}", message.MessageId);
        }
    }

    private static string ComputePayloadHash(EventCreated message)
    {
        var json = JsonSerializer.Serialize(message);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }
}
