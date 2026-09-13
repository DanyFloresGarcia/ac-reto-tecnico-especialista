using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EventService.Infrastructure;

/// <summary>
/// Design-time factory used by `dotnet ef migrations add` when no running host is available
/// to supply the connection string via DI. Only used for tooling; the real connection string
/// at runtime comes from configuration (see EventService.Api/Program.cs).
/// </summary>
public class EventDbContextFactory : IDesignTimeDbContextFactory<EventDbContext>
{
    public EventDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<EventDbContext>();
        optionsBuilder.UseNpgsql(
            "Host=localhost;Port=5432;Database=eventdb;Username=eventservice;Password=eventservice_pw");

        return new EventDbContext(optionsBuilder.Options);
    }
}
