using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;

namespace EventService.Api.IntegrationTests;

public class CreateEventTests : IClassFixture<EventServiceApiFactory>
{
    private readonly EventServiceApiFactory _factory;

    public CreateEventTests(EventServiceApiFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateAuthenticatedClient(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.GenerateToken(role));
        return client;
    }

    [Fact]
    public async Task Post_WithValidBodyAndAdminToken_Returns201WithLocationAndEventDto()
    {
        var client = CreateAuthenticatedClient("Admin");
        var body = new
        {
            name = "Concierto Rock en el Parque",
            date = DateTime.UtcNow.AddDays(30),
            location = "Estadio Nacional, Lima",
            zones = new[]
            {
                new { name = "General", price = 50.00m, capacity = 500 },
                new { name = "VIP", price = 150.00m, capacity = 100 },
            },
        };

        var response = await client.PostAsJsonAsync("/events", body);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();

        var dto = await response.Content.ReadFromJsonAsync<EventDtoResponse>();
        dto.Should().NotBeNull();
        dto!.Name.Should().Be("Concierto Rock en el Parque");
        dto.Status.Should().Be("Draft");
        dto.Zones.Should().HaveCount(2);
    }

    [Fact]
    public async Task Post_WithNoZones_Returns400()
    {
        var client = CreateAuthenticatedClient("Admin");
        var body = new
        {
            name = "Evento sin zonas",
            date = DateTime.UtcNow.AddDays(10),
            location = "Algún lugar",
            zones = Array.Empty<object>(),
        };

        var response = await client.PostAsJsonAsync("/events", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_WithZoneCapacityZero_Returns400()
    {
        var client = CreateAuthenticatedClient("Admin");
        var body = new
        {
            name = "Evento con zona inválida",
            date = DateTime.UtcNow.AddDays(10),
            location = "Algún lugar",
            zones = new[] { new { name = "General", price = 10.0m, capacity = 0 } },
        };

        var response = await client.PostAsJsonAsync("/events", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private record EventDtoResponse(Guid Id, string Name, DateTime Date, string Location, string Status, List<object> Zones);
}
