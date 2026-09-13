using EventService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EventService.Infrastructure.Persistence;

public class EventEntityConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        builder.ToTable("Events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Location).IsRequired().HasMaxLength(300);
        builder.Property(e => e.Date).IsRequired();
        builder.Property(e => e.Status).IsRequired().HasConversion<int>();

        builder.HasMany(e => e.Zones)
            .WithOne()
            .HasForeignKey(z => z.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(Event.Zones))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // Domain events are transient (dispatched within the same unit of work); never persisted.
        builder.Ignore(e => e.DomainEvents);
    }
}
