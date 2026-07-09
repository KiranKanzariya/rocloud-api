using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Settings;

namespace ROCloud.Application.Common.Security;

/// <summary>Cached counter value (reference type so it can be stored via ICacheService).</summary>
public sealed record CounterValue(int Count);

/// <summary>
/// Tracks failed login attempts per identifier and enforces a lockout after N failures
/// (Security:MaxLoginAttempts / Security:LockoutMinutes, guide §10.2). Backed by ICacheService.
/// </summary>
public class LoginAttemptService
{
    private readonly ICacheService _cache;
    private readonly IAppSettings _settings;

    public LoginAttemptService(ICacheService cache, IAppSettings settings)
    {
        _cache = cache;
        _settings = settings;
    }

    /// <summary>Configured lockout duration in minutes — used to word the lockout message.</summary>
    public int LockoutMinutes => _settings.LockoutMinutes;

    private static string Key(string identifier) => $"login_attempts:{identifier}";

    public async Task<bool> IsLockedOutAsync(string identifier, CancellationToken ct = default)
    {
        var value = await _cache.GetAsync<CounterValue>(Key(identifier), ct);
        return (value?.Count ?? 0) >= _settings.MaxLoginAttempts;
    }

    public async Task<int> RecordFailureAsync(string identifier, CancellationToken ct = default)
    {
        var key = Key(identifier);
        var current = (await _cache.GetAsync<CounterValue>(key, ct))?.Count ?? 0;
        var next = current + 1;
        await _cache.SetAsync(key, new CounterValue(next), TimeSpan.FromMinutes(_settings.LockoutMinutes), ct);
        return next;
    }

    public Task ClearAsync(string identifier, CancellationToken ct = default)
        => _cache.RemoveAsync(Key(identifier), ct);
}
