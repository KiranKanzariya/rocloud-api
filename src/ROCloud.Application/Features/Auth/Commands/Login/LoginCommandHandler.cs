using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Security;
using ROCloud.Application.Features.Auth.Common;
using ROCloud.Application.Features.Auth.Services;

namespace ROCloud.Application.Features.Auth.Commands.Login;

public class LoginCommandHandler : IRequestHandler<LoginCommand, AuthResult>
{
    private readonly IAppDbContext _db;
    private readonly IPasswordService _passwords;
    private readonly LoginAttemptService _attempts;
    private readonly AuthTokenIssuer _issuer;

    public LoginCommandHandler(
        IAppDbContext db, IPasswordService passwords, LoginAttemptService attempts, AuthTokenIssuer issuer)
    {
        _db = db;
        _passwords = passwords;
        _attempts = attempts;
        _issuer = issuer;
    }

    public async Task<AuthResult> Handle(LoginCommand request, CancellationToken ct)
    {
        var clientId = $"{request.Email}:{request.TenantSubdomain}".ToLowerInvariant();

        if (await _attempts.IsLockedOutAsync(clientId, ct))
            throw new AccountLockedException(_attempts.LockoutMinutes);

        var tenant = string.IsNullOrWhiteSpace(request.TenantSubdomain)
            ? null
            : await _db.Tenants.IgnoreQueryFilters().Include(t => t.Plan)
                .FirstOrDefaultAsync(t => t.Subdomain == request.TenantSubdomain && !t.IsDeleted, ct);

        var user = tenant is null
            ? null
            : await _db.Users.IgnoreQueryFilters()
                .Include(u => u.Role).ThenInclude(r => r!.RolePermissions).ThenInclude(rp => rp.Permission)
                .FirstOrDefaultAsync(u => u.TenantId == tenant.Id && u.Email == request.Email && !u.IsDeleted, ct);

        if (user is null || user.PasswordHash is null || !user.IsActive
            || !_passwords.Verify(request.Password, user.PasswordHash))
        {
            await _attempts.RecordFailureAsync(clientId, ct);
            // Constant-ish time — don't reveal whether the email exists.
            await Task.Delay(Random.Shared.Next(200, 400), ct);
            throw new InvalidCredentialsException();
        }

        await _attempts.ClearAsync(clientId, ct);

        var permissions = user.Role?.RolePermissions
            .Where(rp => rp.Permission != null)
            .Select(rp => rp.Permission!.Code)
            .ToArray() ?? [];

        return await _issuer.IssueAsync(user, tenant!, permissions, ct);
    }
}
