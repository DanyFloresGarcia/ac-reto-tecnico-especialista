using System.Text.Json;
using EventService.Application.Events;
using EventService.Application.Repositories;
using Microsoft.Extensions.Caching.Distributed;

namespace EventService.Infrastructure.Caching;

// Cache-Aside over Redis (research.md §3): keys events:list / events:{id}, TTL 60s.
public class RedisEventCacheService : IEventCacheService
{
    private const string ListKey = "events:list";
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    private readonly IDistributedCache _cache;

    public RedisEventCacheService(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task<IReadOnlyList<EventDto>?> GetEventListAsync(CancellationToken cancellationToken)
    {
        var cached = await _cache.GetStringAsync(ListKey, cancellationToken);
        return cached is null ? null : JsonSerializer.Deserialize<List<EventDto>>(cached);
    }

    public Task SetEventListAsync(IReadOnlyList<EventDto> events, CancellationToken cancellationToken) =>
        _cache.SetStringAsync(
            ListKey,
            JsonSerializer.Serialize(events),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Ttl },
            cancellationToken);

    public Task InvalidateEventListAsync(CancellationToken cancellationToken) =>
        _cache.RemoveAsync(ListKey, cancellationToken);

    public async Task<EventDto?> GetEventAsync(Guid id, CancellationToken cancellationToken)
    {
        var cached = await _cache.GetStringAsync(EventKey(id), cancellationToken);
        return cached is null ? null : JsonSerializer.Deserialize<EventDto>(cached);
    }

    public Task SetEventAsync(EventDto @event, CancellationToken cancellationToken) =>
        _cache.SetStringAsync(
            EventKey(@event.Id),
            JsonSerializer.Serialize(@event),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Ttl },
            cancellationToken);

    private static string EventKey(Guid id) => $"events:{id}";
}
