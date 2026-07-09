namespace ROCloud.Application.Common.Interfaces;

/// <summary>
/// Caching abstraction (guide §4b.1). Callers use ONLY this interface — never
/// IMemoryCache or IDistributedCache directly — so the in-memory → Redis swap
/// is a one-class change.
/// </summary>
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class;
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default) where T : class;
    Task RemoveAsync(string key, CancellationToken ct = default);
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);

    // Counter operations — used by rate limiting and login attempts
    Task<long> IncrementAsync(string key, long delta = 1, CancellationToken ct = default);
    Task SetExpiryAsync(string key, TimeSpan expiry, CancellationToken ct = default);
}
