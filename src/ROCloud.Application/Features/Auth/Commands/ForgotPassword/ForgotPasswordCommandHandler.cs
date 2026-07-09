using System.Security.Cryptography;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Security;
using ROCloud.Application.Common.Settings;

namespace ROCloud.Application.Features.Auth.Commands.ForgotPassword;

public class ForgotPasswordCommandHandler : IRequestHandler<ForgotPasswordCommand>
{
    private readonly IAppDbContext _db;
    private readonly ICacheService _cache;
    private readonly IEmailService _email;
    private readonly IAppSettings _settings;

    public ForgotPasswordCommandHandler(IAppDbContext db, ICacheService cache, IEmailService email, IAppSettings settings)
    {
        _db = db;
        _cache = cache;
        _email = email;
        _settings = settings;
    }

    public async Task Handle(ForgotPasswordCommand request, CancellationToken ct)
    {
        var tenant = string.IsNullOrWhiteSpace(request.TenantSubdomain)
            ? null
            : await _db.Tenants.IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Subdomain == request.TenantSubdomain && !t.IsDeleted, ct);

        // Only active users may reset — a deactivated user resetting then hitting the login IsActive
        // block would be told "password reset, please sign in" and then fail login (a dead-end).
        var user = tenant is null
            ? null
            : await _db.Users.IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.TenantId == tenant.Id && u.Email == request.Email && !u.IsDeleted && u.IsActive, ct);

        if (user is not null && !string.IsNullOrEmpty(user.Email))
        {
            var ttlMinutes = _settings.PasswordResetTokenTtlMinutes;
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            await _cache.SetAsync($"pwreset:{token}", new PasswordResetToken(user.Id), TimeSpan.FromMinutes(ttlMinutes), ct);

            await _email.SendAsync(
                user.Email,
                "Reset your ROCloud password",
                $"Use this token to reset your password (valid for {ttlMinutes} minutes): {token}",
                ct);
        }

        // Always succeed — never reveal whether the email exists.
    }
}
