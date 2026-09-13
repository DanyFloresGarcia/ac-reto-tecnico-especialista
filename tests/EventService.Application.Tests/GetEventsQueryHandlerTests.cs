using EventService.Application.Events;
using EventService.Application.Repositories;
using FluentAssertions;
using NSubstitute;
using DomainEvent = EventService.Domain.Event;

namespace EventService.Application.Tests;

public class GetEventsQueryHandlerTests
{
    private readonly IEventRepository _repository = Substitute.For<IEventRepository>();
    private readonly IEventCacheService _cache = Substitute.For<IEventCacheService>();

    private GetEventsQueryHandler CreateHandler() => new(_repository, _cache);

    [Fact]
    public async Task Handle_WhenCacheHasList_ReturnsFromCacheWithoutHittingRepository()
    {
        var cached = new List<EventDto> { new(Guid.NewGuid(), "Cached", DateTime.UtcNow, "Lugar", "Draft", new List<ZoneDto>()) };
        _cache.GetEventListAsync(Arg.Any<CancellationToken>()).Returns(cached);

        var result = await CreateHandler().Handle(new GetEventsQuery(), CancellationToken.None);

        result.Should().BeEquivalentTo(cached);
        await _repository.DidNotReceive().GetAllAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenCacheMisses_ReadsFromRepositoryAndPopulatesCache()
    {
        _cache.GetEventListAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<EventDto>?)null);
        var domainEvent = DomainEvent.Create("Concierto", DateTime.UtcNow.AddDays(5), "Lugar", new[] { ("General", 10m, 5) });
        _repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<DomainEvent> { domainEvent });

        var result = await CreateHandler().Handle(new GetEventsQuery(), CancellationToken.None);

        result.Should().ContainSingle(e => e.Name == "Concierto");
        await _cache.Received(1).SetEventListAsync(Arg.Any<IReadOnlyList<EventDto>>(), Arg.Any<CancellationToken>());
    }
}

public class GetEventByIdQueryHandlerTests
{
    private readonly IEventRepository _repository = Substitute.For<IEventRepository>();
    private readonly IEventCacheService _cache = Substitute.For<IEventCacheService>();

    private GetEventByIdQueryHandler CreateHandler() => new(_repository, _cache);

    [Fact]
    public async Task Handle_WhenCacheHasEvent_ReturnsFromCacheWithoutHittingRepository()
    {
        var id = Guid.NewGuid();
        var cached = new EventDto(id, "Cached", DateTime.UtcNow, "Lugar", "Draft", new List<ZoneDto>());
        _cache.GetEventAsync(id, Arg.Any<CancellationToken>()).Returns(cached);

        var result = await CreateHandler().Handle(new GetEventByIdQuery(id), CancellationToken.None);

        result.Should().Be(cached);
        await _repository.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenEventDoesNotExist_ReturnsNull()
    {
        var id = Guid.NewGuid();
        _cache.GetEventAsync(id, Arg.Any<CancellationToken>()).Returns((EventDto?)null);
        _repository.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns((DomainEvent?)null);

        var result = await CreateHandler().Handle(new GetEventByIdQuery(id), CancellationToken.None);

        result.Should().BeNull();
    }
}
