using EventService.Domain;
using FluentAssertions;

namespace EventService.Domain.Tests;

public class EventTests
{
    private static (string Name, decimal Price, int Capacity) ValidZone() => ("General", 50m, 100);

    [Fact]
    public void Create_WithValidData_CreatesEventInDraftStatus()
    {
        var zones = new[] { ("General", 50m, 500), ("VIP", 150m, 100) };

        var @event = Event.Create("Concierto", DateTime.UtcNow.AddDays(30), "Estadio Nacional", zones);

        @event.Status.Should().Be(EventStatus.Draft);
        @event.Zones.Should().HaveCount(2);
    }

    [Fact]
    public void Create_WithEmptyName_ThrowsDomainException()
    {
        var act = () => Event.Create(string.Empty, DateTime.UtcNow.AddDays(1), "Lugar", new[] { ValidZone() });

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_WithNameLongerThan200Characters_ThrowsDomainException()
    {
        var longName = new string('a', 201);

        var act = () => Event.Create(longName, DateTime.UtcNow.AddDays(1), "Lugar", new[] { ValidZone() });

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_WithEmptyLocation_ThrowsDomainException()
    {
        var act = () => Event.Create("Nombre", DateTime.UtcNow.AddDays(1), string.Empty, new[] { ValidZone() });

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_WithNoZones_ThrowsDomainException()
    {
        var act = () => Event.Create("Nombre", DateTime.UtcNow.AddDays(1), "Lugar", Array.Empty<(string, decimal, int)>());

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_WithZoneCapacityZero_ThrowsDomainException()
    {
        var act = () => Event.Create("Nombre", DateTime.UtcNow.AddDays(1), "Lugar", new[] { ("General", 50m, 0) });

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_WithNegativeZonePrice_ThrowsDomainException()
    {
        var act = () => Event.Create("Nombre", DateTime.UtcNow.AddDays(1), "Lugar", new[] { ("General", -1m, 10) });

        act.Should().Throw<DomainException>();
    }
}
