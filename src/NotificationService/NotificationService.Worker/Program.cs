using MassTransit;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using NotificationService.Application.Consumers;
using EventContracts;
using NotificationService.Application.Notifications;
using NotificationService.Application.Repositories;
using NotificationService.Infrastructure;
using NotificationService.Infrastructure.Notifications;
using NotificationService.Infrastructure.Persistence;
using NotificationService.Worker.Observability;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, loggerConfiguration) => loggerConfiguration
    .Enrich.FromLogContext()
    .Enrich.WithSpan()
    .Enrich.WithProperty("service", "NotificationService")
    .MinimumLevel.Information()
    .WriteTo.Console(new RenderedCompactJsonFormatter()));

builder.AddObservability();

builder.Services.AddDbContext<NotificationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("NotificationDb")));

builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();
builder.Services.AddScoped<IEmailSender, MailKitEmailSender>();

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<EventCreatedConsumer>();
    x.AddConsumer<EventCreatedFaultConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        // Must match EventService.Api's cfg.Message<EventCreated>(...) exactly — see the
        // comment there for why (two independent CLR types, same literal exchange name).
        cfg.Message<EventCreated>(m => m.SetEntityName("EventCreated"));

        var rabbitPort = ushort.TryParse(builder.Configuration["RabbitMq:Port"], out var p) ? p : (ushort)5672;
        cfg.Host(builder.Configuration["RabbitMq:Host"] ?? "localhost", rabbitPort, "/", h =>
        {
            h.Username(builder.Configuration["RabbitMq:Username"] ?? "guest");
            h.Password(builder.Configuration["RabbitMq:Password"] ?? "guest");
        });

        // research.md §2: 3 retries, 5s apart. Once exhausted, MassTransit both routes the
        // message to the native `<queue>_error` DLQ and raises Fault<EventCreated>, which
        // EventCreatedFaultConsumer uses to record Status = Failed (FR-010).
        cfg.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));

        cfg.ConfigureEndpoints(context);
    });
});

// RabbitMQ readiness is covered by the bus health check that AddMassTransit registers
// automatically (tagged "ready"), so no separate RabbitMQ health check package is needed here.
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("NotificationDb") ?? string.Empty, tags: new[] { "ready" });

var app = builder.Build();

// research.md §9: apply migrations at startup; fail fast if they don't apply cleanly.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Database.Migrate();
}

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

// research.md §1: scraping *pull* de Prometheus — no bloquea el procesamiento de mensajes (SC-006).
app.MapPrometheusScrapingEndpoint("/metrics");

app.Run();

// Exposes the top-level Program for WebApplicationFactory<Program> in integration tests.
public partial class Program
{
}
