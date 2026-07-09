using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
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

        if (user is null || user.RefreshToken is null || !user.IsActive)
            throw new InvalidCredentialsException();

        var presentedHash = _tokens.HashRefreshToken(request.RefreshToken);
        if (!string.Equals(user.RefreshToken, presentedHash, StringComparison.Ordinal))
        {
            // A rotated/old token was replayed → assume theft and revoke ALL sessions.
            user.RefreshToken = null;
            user.RefreshTokenExpiresAt = null;
            await _db.SaveChangesAsync(ct);
            throw new InvalidCredentialsException();
        }

        if (user.RefreshTokenExpiresAt is null || user.RefreshTokenExpiresAt <= DateTime.UtcNow)
            throw new InvalidCredentialsException();

        var tenant = await _db.Tenants.IgnoreQueryFilters().Include(t => t.Plan)
            .FirstOrDefaultAsync(t => t.Id == user.TenantId, ct);
        if (tenant is null)
            throw new InvalidCredentialsException();

        var permissions = user.Role?.RolePermissions
            .Where(rp => rp.Permission != null)
            .Select(rp => rp.Permission!.Code)
            .ToArray() ?? [];

        // Rotation: a new refresh token is issued and its hash stored, invalidating the old one.
        return await _issuer.IssueAsync(user, tenant, permissions, ct);
    }
}
