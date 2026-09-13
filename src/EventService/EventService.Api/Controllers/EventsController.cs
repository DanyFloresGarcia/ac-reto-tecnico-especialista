using EventService.Api.Middleware;
using EventService.Api.Security;
using EventService.Application.Events;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EventService.Api.Controllers;

[ApiController]
[Route("events")]
public class EventsController : ControllerBase
{
    private readonly ISender _sender;

    public EventsController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>
    /// Crea un nuevo evento con sus zonas (rol Admin). Persiste Event+Zones+OutboxMessage en
    /// una sola transacción y publica <c>EventCreated</c> de forma asíncrona una vez confirmada.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.RequireAdminRole)]
    [EnableRateLimiting("create-event")]
    [ProducesResponseType(typeof(EventDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<EventDto>> Create(
        [FromBody] CreateEventRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = Guid.TryParse(HttpContext.GetCorrelationId(), out var parsed)
            ? parsed
            : Guid.NewGuid();

        var command = new CreateEventCommand(
            request.Name,
            request.Date,
            request.Location,
            request.Zones.Select(z => new CreateEventZoneInput(z.Name, z.Price, z.Capacity)).ToList(),
            correlationId);

        var result = await _sender.Send(command, cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    /// <summary>
    /// Lista todos los eventos (Admin o User). Servido desde caché Redis (TTL 60s) cuando está
    /// vigente — sin paginación en este MVP (ver contracts/events-api.md).
    /// </summary>
    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.RequireAuthenticatedUser)]
    [EnableRateLimiting("read-events")]
    [ProducesResponseType(typeof(IReadOnlyList<EventDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<IReadOnlyList<EventDto>>> GetAll(CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetEventsQuery(), cancellationToken);
        return Ok(result);
    }

    /// <summary>Obtiene el detalle de un evento por id (Admin o User).</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.RequireAuthenticatedUser)]
    [EnableRateLimiting("read-events")]
    [ProducesResponseType(typeof(EventDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<EventDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetEventByIdQuery(id), cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }
}
