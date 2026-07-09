using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Auth.Common;
using ROCloud.Application.Features.Auth.Services;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Auth.Commands.GoogleLogin;

public class GoogleLoginCommandHandler : IRequestHandler<GoogleLoginCommand, AuthResult>
{
    private readonly IAppDbContext _db;
    private readonly IGoogleAuthService _google;
    private readonly AuthTokenIssuer _issuer;

    public GoogleLoginCommandHandler(IAppDbContext db, IGoogleAuthService google, AuthTokenIssuer issuer)
    {
        _db = db;
        _google = google;
        _issuer = issuer;
    }

    public async Task<AuthResult> Handle(GoogleLoginCommand request, CancellationToken ct)
    {
        var info = await _google.ValidateAsync(request.IdToken, ct);
        if (info is null)
            throw new InvalidCredentialsException();

        var tenant = string.IsNullOrWhiteSpace(request.TenantSubdomain)
            ? null
            : await _db.Tenants.IgnoreQueryFilters().Include(t => t.Plan)
                .FirstOrDefaultAsync(t => t.Subdomain == request.TenantSubdomain && !t.IsDeleted, ct);
        if (tenant is null)
            throw new NotFoundException("Tenant", request.TenantSubdomain ?? string.Empty);

        var user = await _db.Users.IgnoreQueryFilters()
            .Include(u => u.Role).ThenInclude(r => r!.RolePermissions).ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(u => u.TenantId == tenant.Id && !u.IsDeleted
                && (u.GoogleId == info.Subject || u.Email == info.Email), ct);

        // Invite-only: Google sign-in never auto-creates a member. The user must already exist in this
        // workspace (invited by an owner, or the owner who registered with Google). New businesses sign
        // up via /api/auth/register-google.
        if (user is null)
            throw new ForbiddenAccessException(
                "No account is linked to this Google sign-in for this workspace. Ask your administrator to invite you.");

        if (user.AuthProvider == AuthProvider.Custom)
        {
            // First Google sign-in for an existing password account — link the two.
            user.GoogleId = info.Subject;
            user.GoogleEmail = info.Email;
            user.AuthProvider = AuthProvider.Both;
        }

        if (!user.IsActive)
            throw new InvalidCredentialsException();

        var permissions = user.Role?.RolePermissions
            .Where(rp => rp.Permission != null)
            .Select(rp => rp.Permission!.Code)
            .ToArray() ?? [];

        return await _issuer.IssueAsync(user, tenant, permissions, ct);
    }
}
