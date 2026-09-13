using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;

namespace EventService.Api.IntegrationTests;

// Full authorization matrix (spec §Historia 3 / SC-006): no token → 401, wrong role → 403,
// correct role → success. Individual happy-path cases are also covered incidentally by
// CreateEventTests/GetEventsTests; this suite is the dedicated, exhaustive matrix.
public class AuthorizationTests : IClassFixture<EventServiceApiFactory>
{
    private readonly EventServiceApiFactory _factory;

    public AuthorizationTests(EventServiceApiFactory factory)
    {
        _factory = factory;
    }

    private static object ValidEventBody() => new
    {
        name = "Evento de prueba de autorización",
        date = DateTime.UtcNow.AddDays(5),
        location = "Algún lugar",
        zones = new[] { new { name = "General", price = 10.0m, capacity = 5 } },
    };

    [Fact]
    public async Task Post_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/events", ValidEventBody());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/events");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Post_WithUserRole_Returns403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _factory.GenerateToken("User"));

        var response = await client.PostAsJsonAsync("/events", ValidEventBody());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Get_WithUserRole_Returns200()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _factory.GenerateToken("User"));

        var response = await client.GetAsync("/events");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Post_WithAdminRole_Returns201()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _factory.GenerateToken("Admin"));

        var response = await client.PostAsJsonAsync("/events", ValidEventBody());

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
