using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Settings;
using ROCloud.Application.Features.Auth.Common;
using ROCloud.Domain.Entities.Platform;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Auth.Services;

/// <summary>
/// Issues an access token + a rotated refresh token and persists the refresh hash/expiry
/// on the user. The refresh token is formatted "{userId}.{random}" so the refresh handler
/// can identify the user and detect reuse of a rotated token (guide §10.3).
///
/// Every session-issuing path funnels through here (password login, Google login, the Google handoff,
/// and refresh), which makes it the one place to refuse a session on a blocked workspace — see
/// <see cref="EnsureTenantAccessible"/>.
/// </summary>
public class AuthTokenIssuer
{
    private const string OwnerRole = "Owner";

    private readonly IAppDbContext _db;
    private readonly ITokenService _tokens;
    private readonly IAppSettings _settings;
    private readonly IDeviceContext _device;

    public AuthTokenIssuer(
        IAppDbContext db, ITokenService tokens, IAppSettings settings, IDeviceContext device)
    {
        _db = db;
        _tokens = tokens;
        _settings = settings;
        _device = device;
    }

    /// <summary>
    /// Issues a session. <paramref name="sessionId"/> continues an existing device (refresh rotating
    /// its token); omitted, it starts a new one — which is what makes signing in on a second device
    /// additive rather than something that displaces the first.
    /// </summary>
    /// <param name="carriedLabel">
    /// The label the previous row in this rotation chain had. Kept so a session keeps its name for its
    /// whole life — a refresh from a client that sends no <c>X-Device</c> must not blank it out — while
    /// a client that does send one still wins, so a renamed phone updates.
    /// </param>
    public async Task<AuthResult> IssueAsync(
        User user, Tenant tenant, IReadOnlyCollection<string> permissions, CancellationToken ct,
        Guid? sessionId = null, string? carriedLabel = null)
    {
        EnsureTenantAccessible(user, tenant);

        var session = sessionId ?? Guid.NewGuid();
        var now = DateTime.UtcNow;
        var label = _device.Label ?? carriedLabel;

        // The sid claim is what lets the sessions list mark one row "this device" — the access token is
        // all a plain GET carries, and without it the caller would be shown a list of their own
        // sessions with no way to tell which one they are looking at it from.
        var access = _tokens.GenerateAccessToken(user, tenant, permissions, sessionId: session);
        var refreshToken = $"{user.Id}.{_tokens.GenerateRefreshToken()}";

        _db.UserSessions.Add(new UserSession
        {
            Id = Guid.NewGuid(),
            TenantId = user.TenantId,
            UserId = user.Id,
            SessionId = session,
            TokenHash = _tokens.HashRefreshToken(refreshToken),
            ExpiresAt = now.AddDays(_settings.RefreshTokenExpiryDays),
            Label = label,
            LastSeenAt = now
        });

        // Deliberately does NOT clear user.RefreshToken. That column is the old single-slot session,
        // and on the deploy that ships this table there are live ones in it; the refresh handler
        // adopts them one at a time. Clearing it here would sign those users out of whichever device
        // they were not currently using — the exact fault this change exists to remove.

        user.LastLoginAt = DateTime.UtcNow;

        await PruneAsync(user.Id, ct);
        await _db.SaveChangesAsync(ct);

        return new AuthResult(access.Token, access.ExpiresAtUtc, refreshToken);
    }

    /// <summary>
    /// Drops this user's spent rows on the way past, so the table stays roughly "one row per live
    /// device" instead of growing by one every hour forever. Signing in is the natural moment: it is
    /// rare, already writing, and scoped to the one user whose rows are being added to.
    /// </summary>
    /// <remarks>
    /// A row is dropped only once its token could no longer be presented — that is, past its own
    /// <c>ExpiresAt</c>, whether or not it was revoked along the way.
    /// <para>
    /// It used to delete revoked rows after a week, which quietly put a seven-day limit on replay
    /// detection: a token stolen and replayed on day eight matched no row at all, so instead of being
    /// recognised as a rotated token coming back — and revoking that device's chain — it was refused
    /// like any random string, and the theft signal was lost. The token stayed presentable for thirty
    /// days, so the memory has to last thirty days too.
    /// </para>
    /// </remarks>
    private async Task PruneAsync(Guid userId, CancellationToken ct)
    {
        // ExecuteDeleteAsync is relational-only; the in-memory provider the tests use has no such
        // path, and pruning is housekeeping, not behaviour, so it is simply skipped there.
        if (!_db.IsRelational) return;

        var now = DateTime.UtcNow;
        await _db.UserSessions
            .Where(s => s.UserId == userId && s.ExpiresAt < now)
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// Refuses a session to a NON-OWNER when the workspace is blocked. TenantMiddleware already 401s
    /// every request from a blocked tenant, so a staff member who is let in lands in an app where each
    /// page fails and the block is not theirs to clear — better to say so at the door.
    ///
    /// The OWNER is always let through: the billing endpoints stay reachable (guide §25) so they can pay
    /// and reactivate. Because refresh runs through here too, a staff session that was open when the
    /// suspension landed ends at its next token refresh rather than lingering.
    ///
    /// Overdue-past-grace is deliberately NOT blocked here: the grace window is config-driven and the
    /// tenant is expected to trade out of it, so staff keep working until the middleware's 402 kicks in.
    /// </summary>
    private static void EnsureTenantAccessible(User user, Tenant tenant)
    {
        var blocked = tenant.Status switch
        {
            TenantStatus.Suspended => true,
            // Cancelled keeps the access already paid for until the period ends (mirrors TenantMiddleware).
            TenantStatus.Cancelled =>
                (tenant.SubscriptionEndsAt ?? tenant.TrialEndsAt) is not { } paidUntil
                || paidUntil < DateTime.UtcNow,
            _ => false
        };

        if (!blocked || string.Equals(user.Role?.Name, OwnerRole, StringComparison.Ordinal))
            return;

        throw new TenantBlockedException(tenant.Status == TenantStatus.Suspended
            ? "This workspace is suspended. Please ask the account owner to renew the subscription."
            : "This workspace's subscription has ended. Please ask the account owner to reactivate it.");
    }
}
