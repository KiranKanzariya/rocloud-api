using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Settings;

namespace ROCloud.Application.Features.Platform.Tenants.Commands.ImpersonateTenant;

/// <summary>
/// Issues a tenant access token for a tenant's Owner so platform staff can open the owner portal
/// as them (guide §26). SuperAdmin only; audited. Access-token-only (no refresh) so the owner's
/// real session/refresh token is untouched — the impersonation expires with the access token.
/// </summary>
/// <remarks>
/// Two things make this containable, and neither was here before:
/// <list type="bullet">
/// <item>It lasts <c>Platform:ImpersonationMinutes</c> (15), not a full hour. A staff member looking at
/// a support ticket does not need a session as long as the owner's own.</item>
/// <item>It carries an <c>act</c> claim naming the operator, so an action taken during impersonation
/// is attributable to a person rather than appearing in the audit trail as the owner doing it
/// themselves.</item>
/// </list>
/// It is also revocable now: the token carries a <c>jti</c>, and <c>POST /api/auth/logout</c> is
/// anonymous and blocklists whatever <c>jti</c> it is presented with — so "Exit impersonation" ends
/// the session immediately instead of leaving it live until it expires.
/// </remarks>
public sealed record ImpersonateTenantCommand(Guid TenantId) : IRequest<ImpersonateResultDto>;

public sealed record ImpersonateResultDto(
    string AccessToken, DateTime ExpiresAtUtc, string Subdomain, string OwnerName,
    // Ready-to-open owner-portal URL that signs the staff member in as the owner. The token rides in the
    // URL fragment (#) so it is never sent to or logged by any server.
    string ImpersonateUrl);

public class ImpersonateTenantCommandHandler : IRequestHandler<ImpersonateTenantCommand, ImpersonateResultDto>
{
    private readonly IAppDbContext _db;
    private readonly ITokenService _tokens;
    private readonly IAppSettings _settings;
    private readonly ICurrentUserService _currentUser;

    public ImpersonateTenantCommandHandler(
        IAppDbContext db, ITokenService tokens, IAppSettings settings, ICurrentUserService currentUser)
    {
        _db = db;
        _tokens = tokens;
        _settings = settings;
        _currentUser = currentUser;
    }

    public async Task<ImpersonateResultDto> Handle(ImpersonateTenantCommand request, CancellationToken ct)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters().Include(t => t.Plan)
            .FirstOrDefaultAsync(t => t.Id == request.TenantId && !t.IsDeleted, ct)
            ?? throw new NotFoundException("Tenant", request.TenantId);

        // The Owner user: match the tenant's owner email, with role + permissions for the token.
        var owner = await _db.Users.IgnoreQueryFilters()
            .Include(u => u.Role).ThenInclude(r => r!.RolePermissions).ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(u => u.TenantId == tenant.Id && u.Email == tenant.OwnerEmail && !u.IsDeleted, ct)
            ?? throw new NotFoundException("Owner user", tenant.Id);

        var permissions = owner.Role?.RolePermissions
            .Where(rp => rp.Permission != null)
            .Select(rp => rp.Permission!.Code)
            .ToArray() ?? [];

        var access = _tokens.GenerateAccessToken(
            owner, tenant, permissions,
            lifetime: TimeSpan.FromMinutes(_settings.ImpersonationMinutes),
            // Falls back to the platform user id when the email claim is absent, so the trail never
            // silently loses the operator — an unattributable impersonation is the thing to avoid.
            actAs: _currentUser.Email ?? _currentUser.UserId?.ToString() ?? "platform");

        var portalBase = _settings.TenantUrlFormat.Replace("{subdomain}", tenant.Subdomain);
        var impersonateUrl = $"{portalBase}/impersonate#token={access.Token}";
        return new ImpersonateResultDto(
            access.Token, access.ExpiresAtUtc, tenant.Subdomain, owner.Name, impersonateUrl);
    }
}
