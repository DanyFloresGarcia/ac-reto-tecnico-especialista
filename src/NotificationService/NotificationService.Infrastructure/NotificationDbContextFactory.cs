using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NotificationService.Infrastructure;

/// <summary>
/// Design-time factory used by `dotnet ef migrations add`. See EventDbContextFactory for rationale.
/// </summary>
public class NotificationDbContextFactory : IDesignTimeDbContextFactory<NotificationDbContext>
{
    public NotificationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<NotificationDbContext>();
        optionsBuilder.UseNpgsql(
            "Host=localhost;Port=5433;Database=notificationdb;Username=notificationservice;Password=notificationservice_pw");

        return new NotificationDbContext(optionsBuilder.Options);
    }
}
