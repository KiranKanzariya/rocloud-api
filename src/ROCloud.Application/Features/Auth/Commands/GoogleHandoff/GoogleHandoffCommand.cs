using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Auth.Common;
using ROCloud.Application.Features.Auth.Services;

namespace ROCloud.Application.Features.Auth.Commands.GoogleHandoff;

/// <summary>
/// Subdomain step of Google sign-in (guide §5): exchanges a one-time handoff token (minted by the apex
/// after Google verification) for a real session on this tenant. Establishes the access token + refresh
/// cookie exactly like a password login. The handoff token is short-lived (~90s) and purpose-scoped.
/// </summary>
/// <param name="Grant">The one-time handoff token minted by the apex step.</param>
/// <param name="Subdomain">
/// The workspace this grant is being redeemed on, from <c>X-Tenant</c> or the host. Null when the
/// host names no workspace, which is not an error — the apex redeems nothing.
/// </param>
public sealed record GoogleHandoffCommand(string Grant, string? Subdomain = null) : IRequest<AuthResult>;

public class GoogleHandoffCommandHandler : IRequestHandler<GoogleHandoffCommand, AuthResult>
{
    private readonly IAppDbContext _db;
    private readonly ITokenService _tokens;
    private readonly AuthTokenIssuer _issuer;

    public GoogleHandoffCommandHandler(IAppDbContext db, ITokenService tokens, AuthTokenIssuer issuer)
    {
        _db = db;
        _tokens = tokens;
        _issuer = issuer;
    }

    public async Task<AuthResult> Handle(GoogleHandoffCommand request, CancellationToken ct)
    {
        var payload = _tokens.ValidateHandoffToken(request.Grant);
        if (payload is null)
            throw new InvalidCredentialsException();

        var user = await _db.Users.IgnoreQueryFilters()
            .Include(u => u.Role).ThenInclude(r => r!.RolePermissions).ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(u => u.Id == payload.UserId && u.TenantId == payload.TenantId && !u.IsDeleted, ct);
        if (user is null || !user.IsActive)
            throw new InvalidCredentialsException();

        var tenant = await _db.Tenants.IgnoreQueryFilters().Include(t => t.Plan)
            .FirstOrDefaultAsync(t => t.Id == payload.TenantId && !t.IsDeleted, ct);
        if (tenant is null)
            throw new NotFoundException("Tenant", payload.TenantId.ToString());

        // The grant names one workspace; redeem it on that workspace's host or not at all. Redeeming
        // an Aqua grant at pani.rocloud.in used to succeed and file an Aqua session under Pani's
        // cookie name, after which every request 403'd on TENANT_MISMATCH and every refresh 401'd on
        // the workspace guard — a tab wedged by a mistake the server could see the whole time.
        if (!string.IsNullOrWhiteSpace(request.Subdomain)
            && !string.Equals(request.Subdomain, tenant.Subdomain, StringComparison.OrdinalIgnoreCase))
            throw new InvalidCredentialsException();

        var permissions = user.Role?.RolePermissions
            .Where(rp => rp.Permission != null)
            .Select(rp => rp.Permission!.Code)
            .ToArray() ?? [];

        return await _issuer.IssueAsync(user, tenant, permissions, ct);
    }
}
