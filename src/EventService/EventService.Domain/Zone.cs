namespace EventService.Domain;

/// <summary>
/// Zone only ever exists as part of an Event's aggregate; it is never created or
/// persisted independently (see Event.Create).
/// </summary>
public class Zone : Entity
{
    public Guid EventId { get; private set; }
    public string Name { get; private set; }
    public decimal Price { get; private set; }
    public int Capacity { get; private set; }

    private Zone(Guid id, Guid eventId, string name, decimal price, int capacity) : base(id)
    {
        EventId = eventId;
        Name = name;
        Price = price;
        Capacity = capacity;
    }

    internal static Zone Create(Guid eventId, string name, decimal price, int capacity)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("El nombre de la zona es requerido.");
        }

        if (capacity <= 0)
        {
            throw new DomainException("La capacidad de la zona debe ser mayor que 0.");
        }

        if (price < 0)
        {
            throw new DomainException("El precio de la zona no puede ser negativo.");
        }

        return new Zone(Guid.NewGuid(), eventId, name, price, capacity);
    }

#pragma warning disable CS8618 // required by EF Core materialization
    private Zone()
    {
    }
#pragma warning restore CS8618
}
