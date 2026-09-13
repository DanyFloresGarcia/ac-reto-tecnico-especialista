using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;

namespace EventService.Api.IntegrationTests;

public class GetEventsTests : IClassFixture<EventServiceApiFactory>
{
    private readonly EventServiceApiFactory _factory;

    public GetEventsTests(EventServiceApiFactory factory)
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
    public async Task Get_ReturnsTheSeedEvent_EvenWithoutAnyPriorPost()
    {
        var client = CreateAuthenticatedClient("User");

        var response = await client.GetAsync("/events");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var events = await response.Content.ReadFromJsonAsync<List<EventDtoResponse>>();
        events.Should().NotBeNull();
        events!.Should().Contain(e => e.Name == "Concierto Rock en el Parque" && e.Zones.Count == 2);
    }

    [Fact]
    public async Task GetById_WithUnknownId_Returns404()
    {
        var client = CreateAuthenticatedClient("User");

        var response = await client.GetAsync($"/events/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetById_ForACreatedEvent_ReturnsIt()
    {
        var adminClient = CreateAuthenticatedClient("Admin");
        var createResponse = await adminClient.PostAsJsonAsync("/events", new
        {
            name = "Evento de detalle",
            date = DateTime.UtcNow.AddDays(5),
            location = "Algún lugar",
            zones = new[] { new { name = "General", price = 20.0m, capacity = 10 } },
        });
        var created = await createResponse.Content.ReadFromJsonAsync<EventDtoResponse>();

        var userClient = CreateAuthenticatedClient("User");
        var response = await userClient.GetAsync($"/events/{created!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<EventDtoResponse>();
        dto!.Name.Should().Be("Evento de detalle");
    }

    private record EventDtoResponse(Guid Id, string Name, DateTime Date, string Location, string Status, List<object> Zones);
}
