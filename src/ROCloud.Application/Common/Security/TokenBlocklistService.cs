using ROCloud.Application.Common.Interfaces;

namespace ROCloud.Application.Common.Security;

/// <summary>Cached marker for a revoked token (reference type for ICacheService).</summary>
public sealed record BlockedToken(bool IsBlocked);

/// <summary>
/// JWT revocation blocklist keyed by jti, with TTL equal to the token's remaining life
/// (guide §10.3). Backed by ICacheService. While in-memory, the blocklist is per-instance
/// and cleared on restart — acceptable for v1 (single instance; restart expires sessions).
/// </summary>
public class TokenBlocklistService
{
    private readonly ICacheService _cache;

    public TokenBlocklistService(ICacheService cache) => _cache = cache;

    public async Task BlockAsync(string jti, DateTime expiresAtUtc, CancellationToken ct = default)
    {
        var ttl = expiresAtUtc - DateTime.UtcNow;
        if (ttl <= TimeSpan.Zero) return;
        await _cache.SetAsync($"blocked:{jti}", new BlockedToken(true), ttl, ct);
    }

    public Task<bool> IsBlockedAsync(string jti, CancellationToken ct = default)
        => _cache.ExistsAsync($"blocked:{jti}", ct);
}
