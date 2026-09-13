using EventService.Application.Events;
using EventContracts;
using EventService.Application.Repositories;
using EventService.Domain;
using FluentAssertions;
using MassTransit;
using NSubstitute;
using DomainEvent = EventService.Domain.Event;

namespace EventService.Application.Tests;

public class CreateEventCommandHandlerTests
{
    private readonly IEventRepository _repository = Substitute.For<IEventRepository>();
    private readonly IEventCacheService _cache = Substitute.For<IEventCacheService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IPublishEndpoint _publishEndpoint = Substitute.For<IPublishEndpoint>();

    private CreateEventCommandHandler CreateHandler() =>
        new(_repository, _cache, _unitOfWork, _publishEndpoint);

    private static CreateEventCommand ValidCommand() => new(
        "Concierto",
        DateTime.UtcNow.AddDays(10),
        "Estadio",
        new[] { new CreateEventZoneInput("General", 50m, 100) },
        Guid.NewGuid());

    [Fact]
    public async Task Handle_WithValidCommand_PersistsEventAndReturnsDto()
    {
        var handler = CreateHandler();

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        result.Name.Should().Be("Concierto");
        result.Status.Should().Be(nameof(EventStatus.Draft));
        await _repository.Received(1).AddAsync(Arg.Any<DomainEvent>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithValidCommand_PublishesEventCreatedMessage()
    {
        var handler = CreateHandler();
        var command = ValidCommand();

        await handler.Handle(command, CancellationToken.None);

        await _publishEndpoint.Received(1).Publish(
            Arg.Is<EventCreated>(m => m.Name == command.Name && m.CorrelationId == command.CorrelationId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithValidCommand_InvalidatesEventListCacheAfterCommit()
    {
        var handler = CreateHandler();

        await handler.Handle(ValidCommand(), CancellationToken.None);

        Received.InOrder(() =>
        {
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
            _cache.InvalidateEventListAsync(Arg.Any<CancellationToken>());
        });
    }
}
