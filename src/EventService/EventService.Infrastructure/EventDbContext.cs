using EventService.Application.Repositories;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using DomainEvent = EventService.Domain.Event;

namespace EventService.Infrastructure;

public class EventDbContext : DbContext, IUnitOfWork
{
    public EventDbContext(DbContextOptions<EventDbContext> options) : base(options)
    {
    }

    public DbSet<DomainEvent> Events => Set<DomainEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EventDbContext).Assembly);

        // MassTransit transactional Outbox tables (research.md §1) — their schema is applied as
        // part of EventService's own migrations, with no external migration mechanism (Constitucion §7.3).
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();

        base.OnModelCreating(modelBuilder);
    }
}
