namespace EventService.Domain;

/// <summary>
/// Aggregate root. Created exclusively via <see cref="Create"/>, which enforces every
/// invariant before the instance exists in memory — no public setters, no partially
/// valid Event can ever be constructed (Constitucion §7.5).
/// </summary>
public class Event : Entity
{
    private const int MaxNameLength = 200;
    private const int MaxLocationLength = 300;

    private readonly List<Zone> _zones = new();

    public string Name { get; private set; }
    public DateTime Date { get; private set; }
    public string Location { get; private set; }
    public EventStatus Status { get; private set; }
    public IReadOnlyCollection<Zone> Zones => _zones.AsReadOnly();

    public IReadOnlyCollection<EventCreatedDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    private readonly List<EventCreatedDomainEvent> _domainEvents = new();

    private Event(Guid id, string name, DateTime date, string location) : base(id)
    {
        Name = name;
        Date = date;
        Location = location;
        Status = EventStatus.Draft;
    }

    public static Event Create(
        string name,
        DateTime date,
        string location,
        IEnumerable<(string Name, decimal Price, int Capacity)> zones)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("El nombre del evento es requerido.");
        }

        if (name.Length > MaxNameLength)
        {
            throw new DomainException($"El nombre del evento no puede exceder {MaxNameLength} caracteres.");
        }

        if (string.IsNullOrWhiteSpace(location))
        {
            throw new DomainException("El lugar del evento es requerido.");
        }

        if (location.Length > MaxLocationLength)
        {
            throw new DomainException($"El lugar del evento no puede exceder {MaxLocationLength} caracteres.");
        }

        var zoneList = zones?.ToList() ?? new List<(string, decimal, int)>();
        if (zoneList.Count == 0)
        {
            throw new DomainException("El evento debe crearse con al menos una zona.");
        }

        var @event = new Event(Guid.NewGuid(), name, date, location);

        foreach (var (zoneName, price, capacity) in zoneList)
        {
            @event._zones.Add(Zone.Create(@event.Id, zoneName, price, capacity));
        }

        @event._domainEvents.Add(new EventCreatedDomainEvent(@event.Id, @event.Name, DateTime.UtcNow));

        return @event;
    }

#pragma warning disable CS8618 // required by EF Core materialization
    private Event()
    {
    }
#pragma warning restore CS8618
}
