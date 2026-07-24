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

    public AuthTokenIssuer(IAppDbContext db, ITokenService tokens, IAppSettings settings)
    {
        _db = db;
        _tokens = tokens;
        _settings = settings;
    }

    public async Task<AuthResult> IssueAsync(
        User user, Tenant tenant, IReadOnlyCollection<string> permissions, CancellationToken ct)
    {
        EnsureTenantAccessible(user, tenant);

        var access = _tokens.GenerateAccessToken(user, tenant, permissions);
        var refreshToken = $"{user.Id}.{_tokens.GenerateRefreshToken()}";

        user.RefreshToken = _tokens.HashRefreshToken(refreshToken);
        user.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(_settings.RefreshTokenExpiryDays);
        user.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return new AuthResult(access.Token, access.ExpiresAtUtc, refreshToken);
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
