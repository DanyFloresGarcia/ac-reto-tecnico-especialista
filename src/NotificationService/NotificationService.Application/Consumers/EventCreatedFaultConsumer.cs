using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.Logging;
using EventContracts;
using NotificationService.Application.Repositories;
using NotificationService.Domain;

namespace NotificationService.Application.Consumers;

/// <summary>
/// Fires once EventCreatedConsumer's retries (research.md §2, UseMessageRetry 3×5s) are
/// exhausted. Records the failure durably (FR-010) — MassTransit separately moves the message
/// to the native `_error` queue (the DLQ referenced in docs/architecture.md).
/// </summary>
public class EventCreatedFaultConsumer : IConsumer<Fault<EventCreated>>
{
    private readonly IAuditLogRepository _repository;
    private readonly ILogger<EventCreatedFaultConsumer> _logger;

    public EventCreatedFaultConsumer(IAuditLogRepository repository, ILogger<EventCreatedFaultConsumer> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<Fault<EventCreated>> context)
    {
        var message = context.Message.Message;
        var payloadHash = ComputePayloadHash(message);

        _logger.LogError(
            "Se agotaron los reintentos para MessageId {MessageId} (EventId {EventId}). Motivo: {Reasons}",
            message.MessageId, message.EventId, string.Join("; ", context.Message.Exceptions.Select(e => e.Message)));

        var auditLog = AuditLog.CreateFailed(
            message.MessageId, message.EventId, message.Name, message.OccurredAt, message.CorrelationId, payloadHash);

        // Same idempotency contract as the happy-path consumer: never overwrite an existing row.
        await _repository.TryAddAsync(auditLog, context.CancellationToken);
    }

    private static string ComputePayloadHash(EventCreated message)
    {
        var json = JsonSerializer.Serialize(message);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }
}
