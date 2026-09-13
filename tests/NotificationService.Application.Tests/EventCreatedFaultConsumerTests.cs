using MassTransit;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Consumers;
using EventContracts;
using NotificationService.Application.Repositories;
using NotificationService.Domain;
using NSubstitute;

namespace NotificationService.Application.Tests;

public class EventCreatedFaultConsumerTests
{
    private readonly IAuditLogRepository _repository = Substitute.For<IAuditLogRepository>();
    private readonly ILogger<EventCreatedFaultConsumer> _logger = Substitute.For<ILogger<EventCreatedFaultConsumer>>();

    private EventCreatedFaultConsumer CreateConsumer() => new(_repository, _logger);

    [Fact]
    public async Task Consume_WhenRetriesExhausted_MarksAuditLogAsFailed()
    {
        var message = new EventCreated
        {
            MessageId = Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            Name = "Concierto",
            OccurredAt = DateTime.UtcNow,
            CorrelationId = Guid.NewGuid(),
            Version = 1,
        };

        var fault = Substitute.For<Fault<EventCreated>>();
        fault.Message.Returns(message);
        fault.Exceptions.Returns(Array.Empty<ExceptionInfo>());

        var context = Substitute.For<ConsumeContext<Fault<EventCreated>>>();
        context.Message.Returns(fault);
        context.CancellationToken.Returns(CancellationToken.None);

        _repository.TryAddAsync(Arg.Any<AuditLog>(), Arg.Any<CancellationToken>()).Returns(true);

        await CreateConsumer().Consume(context);

        await _repository.Received(1).TryAddAsync(
            Arg.Is<AuditLog>(a => a.MessageId == message.MessageId && a.Status == AuditLogStatus.Failed),
            Arg.Any<CancellationToken>());
    }
}
