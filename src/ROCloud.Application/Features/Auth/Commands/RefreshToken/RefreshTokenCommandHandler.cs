using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Security;
using ROCloud.Application.Features.Auth.Common;
using ROCloud.Application.Features.Auth.Services;

namespace ROCloud.Application.Features.Auth.Commands.RefreshToken;

public class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, AuthResult>
{
    private readonly IAppDbContext _db;
    private readonly ITokenService _tokens;
    private readonly AuthTokenIssuer _issuer;

    public RefreshTokenCommandHandler(IAppDbContext db, ITokenService tokens, AuthTokenIssuer issuer)
    {
        _db = db;
        _tokens = tokens;
        _issuer = issuer;
    }

    public async Task<AuthResult> Handle(RefreshTokenCommand request, CancellationToken ct)
    {
        // Refresh token format: "{userId}.{random}"
        var dot = request.RefreshToken?.IndexOf('.') ?? -1;
        if (request.RefreshToken is null || dot <= 0
            || !Guid.TryParse(request.RefreshToken[..dot], out var userId))
            throw new InvalidCredentialsException();

        var user = await _db.Users.IgnoreQueryFilters()
            .Include(u => u.Role).ThenInclude(r => r!.RolePermissions).ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, ct);

        if (user is null || !user.IsActive)
            throw new InvalidCredentialsException();

        await EnsureWorkspaceMatchesAsync(request.Subdomain, user.TenantId, ct);

        var presentedHash = _tokens.HashRefreshToken(request.RefreshToken);
        var now = DateTime.UtcNow;

        var session = await _db.UserSessions
            .FirstOrDefaultAsync(s => s.UserId == userId && s.TokenHash == presentedHash, ct);

        if (session is null)
            return await AdoptLegacySessionAsync(user, presentedHash, now, ct);

        if (session.RevokedAt is not null)
        {
            // This token was rotated away and has come back. Either a copy was taken, or a client is
            // retrying with a token it should have replaced — in both cases this device's chain ends
            // here. Only its chain: the whole reason this table exists is that one device must not be
            // able to sign another one out.
            await UserSessions.RevokeChainAsync(_db, session.SessionId, ct);
            await _db.SaveChangesAsync(ct);
            throw new InvalidCredentialsException();
        }

        if (session.ExpiresAt <= now)
            throw new InvalidCredentialsException();

        // Rotation: this row stops being the live token, and the issuer writes the next one under the
        // same SessionId, so the device keeps its identity across the swap.
        session.RevokedAt = now;

        return await IssueForAsync(user, session.SessionId, ct);
    }

    /// <summary>
    /// Refuses to restore a session onto a workspace it does not belong to.
    ///
    /// The refresh cookie belongs to the API host, not to a tenant's subdomain, so the browser sends
    /// it from ANY workspace under the domain — and this endpoint is excluded from TenantMiddleware,
    /// which is what checks the JWT's tenant against the host everywhere else. Together those meant
    /// that opening <c>pani.rocloud.in</c> while holding an Aqua session refreshed it happily and put
    /// Aqua's books behind Pani's address bar. No tenant ever saw another's data — the token was
    /// honestly Aqua's — but the URL said something false, which is its own kind of wrong.
    ///
    /// A 401 is the right answer rather than a mismatch error: the caller genuinely has no session on
    /// THIS workspace, and the portal already treats a failed refresh as "show the sign-in page".
    /// Silence when no subdomain is supplied — the apex sign-in page and the handoff both refresh
    /// from a host that names no tenant, and neither is doing anything wrong.
    private async Task EnsureWorkspaceMatchesAsync(
        string? subdomain, Guid tenantId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(subdomain)) return;

        var requested = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Subdomain == subdomain && !t.IsDeleted, ct);

        // An unknown subdomain names no workspace to be wrong about.
        if (requested is not null && requested.Id != tenantId)
            throw new InvalidCredentialsException();
    }

    /// TRANSITIONAL — delete once every live session predates the user_sessions table by more than
    /// Jwt:RefreshTokenExpiryDays (30 days after the deploy that shipped it).
    ///
    /// Before this table there was one token per user, in users.refresh_token. Those sessions are
    /// live at the moment of deploy, and rejecting them would sign every user of both products out
    /// at once. Instead the first refresh that presents one is honoured, exactly once, and hands
    /// that device a real session row; the column is cleared as it goes, so it cannot be replayed.
    /// </summary>
    private async Task<AuthResult> AdoptLegacySessionAsync(
        Domain.Entities.Tenant.User user, string presentedHash, DateTime now, CancellationToken ct)
    {
        var matches = user.RefreshToken is not null
                      && string.Equals(user.RefreshToken, presentedHash, StringComparison.Ordinal)
                      && user.RefreshTokenExpiresAt > now;

        if (!matches)
            throw new InvalidCredentialsException();

        user.RefreshToken = null;
        user.RefreshTokenExpiresAt = null;

        return await IssueForAsync(user, sessionId: null, ct);
    }

    private async Task<AuthResult> IssueForAsync(
        Domain.Entities.Tenant.User user, Guid? sessionId, CancellationToken ct)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters().Include(t => t.Plan)
            .FirstOrDefaultAsync(t => t.Id == user.TenantId, ct);
        if (tenant is null)
            throw new InvalidCredentialsException();

        var permissions = user.Role?.RolePermissions
            .Where(rp => rp.Permission != null)
            .Select(rp => rp.Permission!.Code)
            .ToArray() ?? [];

        return await _issuer.IssueAsync(user, tenant, permissions, ct, sessionId);
    }
}
