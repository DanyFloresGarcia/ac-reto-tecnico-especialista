using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Consumers;
using EventContracts;
using NotificationService.Application.Notifications;
using NotificationService.Application.Repositories;
using NotificationService.Domain;
using NSubstitute;

namespace NotificationService.Application.Tests;

public class EventCreatedConsumerTests
{
    private readonly IAuditLogRepository _repository = Substitute.For<IAuditLogRepository>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly ILogger<EventCreatedConsumer> _logger = Substitute.For<ILogger<EventCreatedConsumer>>();

    private EventCreatedConsumer CreateConsumer() => new(_repository, _emailSender, _logger);

    private static ConsumeContext<EventCreated> MockContext(EventCreated message)
    {
        var context = Substitute.For<ConsumeContext<EventCreated>>();
        context.Message.Returns(message);
        context.CancellationToken.Returns(CancellationToken.None);
        return context;
    }

    private static EventCreated NewMessage() => new()
    {
        MessageId = Guid.NewGuid(),
        EventId = Guid.NewGuid(),
        Name = "Concierto",
        OccurredAt = DateTime.UtcNow,
        CorrelationId = Guid.NewGuid(),
        Version = 1,
    };

    [Fact]
    public async Task Consume_WhenMessageIdNotSeenBefore_SimulatesEmailAndInsertsProcessedRecord()
    {
        var message = NewMessage();
        _repository.GetByMessageIdAsync(message.MessageId, Arg.Any<CancellationToken>()).Returns((AuditLog?)null);
        _repository.TryAddAsync(Arg.Any<AuditLog>(), Arg.Any<CancellationToken>()).Returns(true);

        await CreateConsumer().Consume(MockContext(message));

        await _emailSender.Received(1).SendEventCreatedNotificationAsync(
            message.EventId, message.Name, message.OccurredAt, Arg.Any<CancellationToken>());
        await _repository.Received(1).TryAddAsync(
            Arg.Is<AuditLog>(a => a.MessageId == message.MessageId && a.Status == AuditLogStatus.Processed),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_WhenMessageIdAlreadyProcessedWithSamePayload_SkipsReprocessing()
    {
        var message = NewMessage();
        var payloadHash = ComputeHashLikeConsumer(message);
        var existing = AuditLog.CreateProcessed(message.MessageId, message.EventId, message.Name, message.OccurredAt, message.CorrelationId, payloadHash);
        _repository.GetByMessageIdAsync(message.MessageId, Arg.Any<CancellationToken>()).Returns(existing);

        await CreateConsumer().Consume(MockContext(message));

        await _emailSender.DidNotReceive().SendEventCreatedNotificationAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().TryAddAsync(Arg.Any<AuditLog>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_WhenDuplicateRaceLosesUniqueConstraint_DoesNotThrow()
    {
        var message = NewMessage();
        _repository.GetByMessageIdAsync(message.MessageId, Arg.Any<CancellationToken>()).Returns((AuditLog?)null);
        _repository.TryAddAsync(Arg.Any<AuditLog>(), Arg.Any<CancellationToken>()).Returns(false);

        var act = async () => await CreateConsumer().Consume(MockContext(message));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Consume_WhenPayloadDiffersUnderSameMessageId_DoesNotOverwriteExisting()
    {
        var message = NewMessage();
        var existing = AuditLog.CreateProcessed(
            message.MessageId, message.EventId, "Nombre distinto", message.OccurredAt, message.CorrelationId, "different-hash");
        _repository.GetByMessageIdAsync(message.MessageId, Arg.Any<CancellationToken>()).Returns(existing);

        await CreateConsumer().Consume(MockContext(message));

        await _repository.DidNotReceive().TryAddAsync(Arg.Any<AuditLog>(), Arg.Any<CancellationToken>());
    }

    private static string ComputeHashLikeConsumer(EventCreated message)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(message);
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }
}
