namespace EventService.Domain;

/// <summary>
/// Only Draft is reachable in this MVP (no endpoint transitions state); Published/Cancelled
/// exist so the data contract does not break when a future phase adds those transitions.
/// </summary>
public enum EventStatus
{
    Draft = 0,
    Published = 1,
    Cancelled = 2,
}
