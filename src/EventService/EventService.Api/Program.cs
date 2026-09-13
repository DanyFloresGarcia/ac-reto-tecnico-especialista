using System.Text;
using System.Threading.RateLimiting;
using EventService.Api.Middleware;
using EventService.Api.Security;
using EventService.Application.Behaviors;
using EventContracts;
using EventService.Application.Repositories;
using EventService.Infrastructure;
using EventService.Infrastructure.Caching;
using EventService.Infrastructure.Persistence;
using FluentValidation;
using MassTransit;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, loggerConfiguration) => loggerConfiguration
    .Enrich.FromLogContext()
    .Enrich.WithProperty("service", "EventService")
    .MinimumLevel.Information()
    .WriteTo.Console(new RenderedCompactJsonFormatter()));

// FR-022 / research.md §4b: fail fast if the JWT signing key is missing or too short.
// Reads `configuration` lazily (never captured eagerly) so it always sees the final,
// fully-merged configuration — including overrides a test host layers in after
// WebApplication.CreateBuilder returns but before the app actually starts serving requests.
static SymmetricSecurityKey GetValidatedSigningKey(IConfiguration configuration)
{
    var value = configuration["Jwt:SigningKey"];
    if (string.IsNullOrEmpty(value) || Encoding.UTF8.GetByteCount(value) < 32)
    {
        throw new InvalidOperationException(
            "Jwt:SigningKey (env var Jwt__SigningKey) must be set and be at least 32 bytes long. " +
            "Refusing to start with a missing or insecure default (spec FR-022).");
    }

    return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(value));
}

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    var securityScheme = new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Pegar el JWT de demo (ver README) sin el prefijo 'Bearer '.",
    };
    options.AddSecurityDefinition("Bearer", securityScheme);
    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        { securityScheme, Array.Empty<string>() },
    });

    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }
});

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddDbContext<EventDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("EventDb")));

builder.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<EventDbContext>());
builder.Services.AddScoped<IEventRepository, EventRepository>();
builder.Services.AddScoped<IEventCacheService, RedisEventCacheService>();
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379";
});

var applicationAssembly = typeof(ValidationBehavior<,>).Assembly;
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(applicationAssembly));
builder.Services.AddValidatorsFromAssembly(applicationAssembly);
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

builder.Services.AddMassTransit(x =>
{
    // Transactional Outbox (research.md §1): EventCreated is persisted in the same EF Core
    // transaction as Event/Zone, and published to RabbitMQ only after that transaction commits.
    x.AddEntityFrameworkOutbox<EventDbContext>(o =>
    {
        o.UsePostgres();
        o.UseBusOutbox();
    });

    // Consumers are not registered here: EventService only publishes EventCreated (US1);
    // it never consumes messages (Constitucion §4 — no synchronous coupling either way).
    x.UsingRabbitMq((context, cfg) =>
    {
        // EventService.Application.Messages.EventCreated and NotificationService's own copy of
        // it are deliberately two separate CLR types (no shared assembly — Constitucion
        // §4/§7.3). MassTransit's default RabbitMQ topology names the exchange after a type's
        // full CLR name, which differs between the two namespaces and would silently never
        // deliver the message. Pinning the same literal entity name on both sides is what
        // actually makes them the same exchange on the wire.
        cfg.Message<EventCreated>(m => m.SetEntityName("EventCreated"));

        var rabbitPort = ushort.TryParse(builder.Configuration["RabbitMq:Port"], out var p) ? p : (ushort)5672;
        cfg.Host(builder.Configuration["RabbitMq:Host"] ?? "localhost", rabbitPort, "/", h =>
        {
            h.Username(builder.Configuration["RabbitMq:Username"] ?? "guest");
            h.Password(builder.Configuration["RabbitMq:Password"] ?? "guest");
        });
        cfg.ConfigureEndpoints(context);
    });
});

// RabbitMQ readiness is covered by the bus health check that AddMassTransit registers
// automatically (tagged "ready"), so no separate RabbitMQ health check package is needed here.
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("EventDb") ?? string.Empty, tags: new[] { "ready" })
    .AddRedis(builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379", tags: new[] { "ready" });

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = GetValidatedSigningKey(builder.Configuration),
            // research.md §4b: pin the algorithm explicitly to prevent "alg" confusion attacks.
            ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AuthorizationPolicies.RequireAdminRole, policy => policy.RequireRole(AppRoles.Admin))
    .AddPolicy(AuthorizationPolicies.RequireAuthenticatedUser, policy => policy.RequireAuthenticatedUser());

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(builder.Configuration["Cors:FrontendOrigin"] ?? "http://localhost:5173")
        .WithMethods("GET", "POST")
        .WithHeaders("Content-Type", "Authorization", CorrelationIdMiddleware.HeaderName));
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("create-event", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 20,
        }));

    options.AddPolicy("read-events", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 100,
        }));
});

var app = builder.Build();

// Eager fail-fast (FR-022): validate against the final configuration once at startup,
// before accepting any request, rather than waiting for the JwtBearer handler's lazy first use.
GetValidatedSigningKey(app.Configuration);

// research.md §9: apply migrations at startup; fail fast (non-zero exit, no partial state)
// if they don't apply cleanly, so Docker Compose reports the container as unhealthy instead
// of accepting traffic against an inconsistent schema.
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<EventDbContext>();
    dbContext.Database.Migrate();

    // FR-020: seed one example event with a fixed, known shape so GET /events is verifiable
    // right after the very first startup, without requiring a manual POST first.
    if (!dbContext.Events.Any())
    {
        var seedEvent = EventService.Domain.Event.Create(
            "Concierto Rock en el Parque",
            DateTime.UtcNow.AddDays(30),
            "Estadio Nacional, Lima",
            new (string, decimal, int)[]
            {
                ("General", 50.00m, 500),
                ("VIP", 150.00m, 100),
            });

        dbContext.Events.Add(seedEvent);
        dbContext.SaveChanges();
    }
}

app.UseExceptionHandler();
app.UseCorrelationId();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.Run();

// Exposes the top-level Program for WebApplicationFactory<Program> in integration tests.
public partial class Program
{
}
