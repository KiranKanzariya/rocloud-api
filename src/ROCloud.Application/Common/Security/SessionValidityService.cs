using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;

namespace ROCloud.Application.Common.Security;

/// <summary>Cached <c>users.sessions_valid_from</c> (reference type so ICacheService can hold it).</summary>
public sealed record SessionCutoff(DateTime? ValidFrom);

/// <summary>Cached "is this device still signed in" answer.</summary>
public sealed record SessionLiveness(bool IsLive);

/// <summary>
/// Account-wide access-token revocation: refuses any access token issued before the moment a user's
/// sessions were invalidated.
/// </summary>
/// <remarks>
/// <para>
/// Revoking a refresh token was never enough. A password reset, a deactivation and a deletion all
/// called <see cref="UserSessions.RevokeAllAsync"/> — which ends the refresh side only, leaving every
/// ALREADY-ISSUED access token valid until it expired on its own. With a 60-minute access token that
/// meant a sacked driver, a deleted user, or an account whose password had just been reset because it
/// was compromised kept full API access for up to an hour, on every device at once. Role and
/// permission changes landed the same way, since permissions are baked into the token.
/// </para>
/// <para>
/// <b>Why a timestamp and not a blocklist.</b> A blocklist can only name tokens you have seen; these
/// are tokens already in the wild, whose <c>jti</c> nobody recorded. A per-user cutoff invalidates all
/// of them at once — including ones issued to devices no longer reachable — and it lives on the user
/// row, so unlike the in-memory blocklist it survives a restart and holds across instances.
/// </para>
/// <para>
/// <b>Why the short cache.</b> This runs on every authenticated request, so it cannot be a database
/// read each time. The cutoff is cached for <see cref="CacheSeconds"/>, which bounds how long a
/// revocation can take to bite — a minute, against the hour it takes to do nothing. Anything longer
/// and, on a second instance, the block would arrive around the same time the token expired anyway,
/// which is the same as not having it. There is deliberately no cache invalidation on write: with the
/// TTL this short it buys a few seconds and costs a coupling between every revoking command and this
/// cache, which is exactly the kind of thing one caller eventually forgets.
/// </para>
/// <para>
/// The stamp is set by <see cref="UserSessions.RevokeAllAsync"/>, so nothing can end a user's refresh
/// chains without also ending their access tokens.
/// </para>
/// </remarks>
public class SessionValidityService
{
    /// <summary>How long a user's cutoff is trusted from cache. Also the worst-case revocation delay.</summary>
    private const int CacheSeconds = 60;

    /// <summary>
    /// Slack for clock drift between the token's issuer and this check. Without it a token issued in
    /// the same second as a stamp could be refused, which would sign a user out of the very session
    /// they just created by resetting their password.
    /// </summary>
    private static readonly TimeSpan Skew = TimeSpan.FromSeconds(5);

    private readonly IAppDbContext _db;
    private readonly ICacheService _cache;

    public SessionValidityService(IAppDbContext db, ICacheService cache)
    {
        _db = db;
        _cache = cache;
    }

    private static string Key(Guid userId) => $"sessions_valid_from:{userId}";

    /// <summary>
    /// True when an access token issued at <paramref name="issuedAtUtc"/> is still honoured for this
    /// user. Unknown users pass — they are rejected by ordinary authorization, and failing here would
    /// turn a missing row into a confusing 401 on a valid signature.
    /// </summary>
    public async Task<bool> IsAcceptableAsync(Guid userId, DateTime issuedAtUtc, CancellationToken ct = default)
    {
        var cutoff = await CutoffAsync(userId, ct);
        return cutoff is null || issuedAtUtc.Add(Skew) >= cutoff.Value;
    }

    /// <summary>
    /// Whether the device a token was issued to is still signed in. False once its chain is revoked.
    /// </summary>
    /// <remarks>
    /// Revoking a chain stops that device REFRESHING, but its current access token would otherwise
    /// keep working for the rest of its hour — so "sign out this device" pressed from another one
    /// would not actually sign anything out for up to an hour, which is exactly the wrong answer when
    /// the reason for pressing it is that somebody else has the phone. Cached like the cutoff, so it
    /// bites within a minute and costs at most one indexed read per session per minute.
    /// </remarks>
    public async Task<bool> IsSessionLiveAsync(Guid userId, Guid sessionId, CancellationToken ct = default)
    {
        var cached = await _cache.GetAsync<SessionLiveness>(SessionKey(sessionId), ct);
        if (cached is not null) return cached.IsLive;

        // Filtered on user_id AND session_id so it uses idx_user_sessions_user_session. On
        // session_id alone Postgres cannot use that index — it is not the leading column — and this
        // runs on every authenticated request, so it would have degraded to a scan as the table grew.
        var live = await _db.UserSessions
            .AnyAsync(s => s.UserId == userId && s.SessionId == sessionId && s.RevokedAt == null, ct);

        await _cache.SetAsync(
            SessionKey(sessionId), new SessionLiveness(live), TimeSpan.FromSeconds(CacheSeconds), ct);
        return live;
    }

    /// <summary>
    /// Records straight away that a device has been signed out, instead of waiting for its cache
    /// entry to lapse.
    /// </summary>
    /// <remarks>
    /// An optimisation, not the correctness mechanism — <see cref="CacheSeconds"/> still bounds how
    /// stale any answer can be, so forgetting to call this delays a revoke rather than losing it.
    /// That split is deliberate: correctness from the TTL, speed from priming.
    /// <para>
    /// It matters because of who presses the button. "Sign out this device" is reached for when
    /// somebody else has the phone, and a minute is a long time to watch a screen you no longer
    /// trust. The web never had this wait — the portal holds its access token in memory only, so any
    /// reload goes through refresh, which reads the row directly.
    /// </para>
    /// </remarks>
    public Task MarkSessionRevokedAsync(Guid sessionId, CancellationToken ct = default)
        => _cache.SetAsync(
            SessionKey(sessionId), new SessionLiveness(false), TimeSpan.FromSeconds(CacheSeconds), ct);

    private static string SessionKey(Guid sessionId) => $"session_live:{sessionId}";

    /// <summary>
    /// The same check for a platform staff token. Separate because a platform token's <c>sub</c> names
    /// a <c>PlatformUser</c>, which is a different table with no row in <c>users</c>.
    /// </summary>
    /// <remarks>
    /// Worth having even though there are far fewer of these accounts: a platform token reaches every
    /// workspace, so a reset or a deactivation leaving one alive for another hour is the widest version
    /// of this problem, not the narrowest.
    /// </remarks>
    public async Task<bool> IsPlatformTokenAcceptableAsync(
        Guid platformUserId, DateTime issuedAtUtc, CancellationToken ct = default)
    {
        var key = $"platform_sessions_valid_from:{platformUserId}";
        var cached = await _cache.GetAsync<SessionCutoff>(key, ct);

        DateTime? cutoff;
        if (cached is not null)
        {
            cutoff = cached.ValidFrom;
        }
        else
        {
            cutoff = await _db.PlatformUsers
                .Where(u => u.Id == platformUserId)
                .Select(u => u.SessionsValidFrom)
                .FirstOrDefaultAsync(ct);
            await _cache.SetAsync(key, new SessionCutoff(cutoff), TimeSpan.FromSeconds(CacheSeconds), ct);
        }

        return cutoff is null || issuedAtUtc.Add(Skew) >= cutoff.Value;
    }

    private async Task<DateTime?> CutoffAsync(Guid userId, CancellationToken ct)
    {
        var cached = await _cache.GetAsync<SessionCutoff>(Key(userId), ct);
        if (cached is not null) return cached.ValidFrom;

        var validFrom = await _db.Users.IgnoreQueryFilters()
            .Where(u => u.Id == userId)
            .Select(u => u.SessionsValidFrom)
            .FirstOrDefaultAsync(ct);

        await _cache.SetAsync(Key(userId), new SessionCutoff(validFrom), TimeSpan.FromSeconds(CacheSeconds), ct);
        return validFrom;
    }

}
