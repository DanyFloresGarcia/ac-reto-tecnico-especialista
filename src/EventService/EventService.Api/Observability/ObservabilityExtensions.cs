using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace EventService.Api.Observability;

/// <summary>
/// Wires OpenTelemetry traces + metrics for EventService (Spec 02, research.md §1). A
/// cross-cutting concern registered from Program.cs — never touches Domain/Application (FR-015).
/// </summary>
public static class ObservabilityExtensions
{
    public const string ServiceName = "EventService";

    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder)
    {
        // research.md §1: endpoint read from configuration (Otel__ExporterEndpoint), same pattern
        // as RabbitMq__Host/Redis__ConnectionString — never hardcoded (resolves /speckit-analyze I1).
        var otlpEndpoint = new Uri(builder.Configuration["Otel:ExporterEndpoint"] ?? "http://tempo:4317");

        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(ServiceName)
            .AddAttributes(
            [
                new KeyValuePair<string, object>("service.namespace", "plataforma-eventos"),
                new KeyValuePair<string, object>("deployment.environment", "local"),
            ]);

        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing => tracing
                .SetResourceBuilder(resourceBuilder)
                // research.md §11: 100% sampling — volumen bajo esperado en demo/pruebas manuales.
                .SetSampler(new AlwaysOnSampler())
                // MassTransit 8.x emite su propio ActivitySource nativo (research.md §2): basta con
                // registrarlo para que los spans de publish/consume aparezcan y propaguen traceparent
                // automáticamente por RabbitMQ, sin tocar EventService.Application.
                .AddSource("MassTransit")
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddEntityFrameworkCoreInstrumentation()
                // research.md §4: instrumenta el mismo IConnectionMultiplexer que RedisEventCacheService
                // (registrado como singleton en Program.cs) — resuelto automáticamente desde el
                // IServiceProvider, sin pasar la conexión explícitamente.
                .AddRedisInstrumentation()
                .AddOtlpExporter(o => o.Endpoint = otlpEndpoint))
            .WithMetrics(metrics => metrics
                .SetResourceBuilder(resourceBuilder)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                // Exporter Prometheus en modo *pull* — no bloquea el request path (SC-006).
                .AddPrometheusExporter());

        return builder;
    }
}
