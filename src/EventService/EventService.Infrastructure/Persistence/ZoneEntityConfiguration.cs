using EventService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EventService.Infrastructure.Persistence;

public class ZoneEntityConfiguration : IEntityTypeConfiguration<Zone>
{
    public void Configure(EntityTypeBuilder<Zone> builder)
    {
        builder.ToTable("Zones");
        builder.HasKey(z => z.Id);
        builder.Property(z => z.Name).IsRequired();
        // Fixed precision/scale (data-model.md §Zone) to avoid rounding ambiguity between domain and storage.
        builder.Property(z => z.Price).HasColumnType("decimal(10,2)");
        builder.Property(z => z.Capacity).IsRequired();

        // Supports loading zones by event (GetEventByIdQuery) — data-model.md §Zone.
        builder.HasIndex(z => z.EventId);
    }
}
