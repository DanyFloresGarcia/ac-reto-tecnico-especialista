using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationService.Domain;

namespace NotificationService.Infrastructure.Persistence;

public class AuditLogEntityConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLog");
        builder.HasKey(a => a.Id);

        // The real idempotency guarantee (FR-009, data-model.md §AuditLog): a DB-level unique
        // index, not just the application's SELECT-before-INSERT fast path.
        builder.HasIndex(a => a.MessageId).IsUnique();

        builder.Property(a => a.EventName).IsRequired();
        builder.Property(a => a.PayloadHash).IsRequired();
        builder.Property(a => a.Status).HasConversion<string>().IsRequired();
    }
}
