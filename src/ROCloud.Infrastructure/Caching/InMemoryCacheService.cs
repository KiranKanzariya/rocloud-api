using Microsoft.Extensions.Caching.Memory;
using ROCloud.Application.Common.Interfaces;

namespace ROCloud.Infrastructure.Caching;

/// <summary>
/// v1 in-memory implementation of <see cref="ICacheService"/> (guide §4b.2).
/// Registered as a singleton. Swap to a Redis implementation later without
/// touching any caller. Single-instance only while on this implementation.
/// </summary>
public class InMemoryCacheService : ICacheService
{
    private readonly IMemoryCache _cache;
    private readonly TimeSpan DefaultExpiry;

    public InMemoryCacheService(IMemoryCache cache, TimeSpan? defaultExpiry = null)
    {
        _cache = cache;
        DefaultExpiry = defaultExpiry ?? TimeSpan.FromMinutes(30);
    }

    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class
        => Task.FromResult(_cache.TryGetValue<T>(key, out var value) ? value : null);

    public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default) where T : class
    {
        _cache.Set(key, value, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiry ?? DefaultExpiry,
            Size = 1 // required when MemoryCache has a size limit
        });
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        _cache.Remove(key);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
        => Task.FromResult(_cache.TryGetValue(key, out _));

    public Task<long> IncrementAsync(string key, long delta = 1, CancellationToken ct = default)
    {
        var current = _cache.TryGetValue<long>(key, out var existing) ? existing : 0L;
        var next = current + delta;
        _cache.Set(key, next, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = DefaultExpiry,
            Size = 1
        });
        return Task.FromResult(next);
    }

    public Task SetExpiryAsync(string key, TimeSpan expiry, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(key, out var value))
        {
            _cache.Set(key, value, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiry,
                Size = 1
            });
        }
        return Task.CompletedTask;
    }
}
