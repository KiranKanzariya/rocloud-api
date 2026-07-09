using System.Security.Cryptography;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Security;
using ROCloud.Application.Common.Settings;

namespace ROCloud.Application.Features.Platform.Auth.Commands.PlatformForgotPassword;

/// <summary>
/// Emails a password-reset token to a platform staff member (super-admin portal, guide §26).
/// Separate cache namespace ("platform-pwreset:") from the tenant reset flow so tokens can't cross
/// over. Only active users are emailed — a deactivated admin resetting then failing login would be a
/// dead-end. Always succeeds regardless, so it never reveals whether an email exists.
/// </summary>
public sealed record PlatformForgotPasswordCommand(string Email) : IRequest;

public class PlatformForgotPasswordCommandHandler : IRequestHandler<PlatformForgotPasswordCommand>
{
    internal const string CacheKeyPrefix = "platform-pwreset:";

    private readonly IAppDbContext _db;
    private readonly ICacheService _cache;
    private readonly IEmailService _email;
    private readonly IAppSettings _settings;

    public PlatformForgotPasswordCommandHandler(
        IAppDbContext db, ICacheService cache, IEmailService email, IAppSettings settings)
    {
        _db = db;
        _cache = cache;
        _email = email;
        _settings = settings;
    }

    public async Task Handle(PlatformForgotPasswordCommand request, CancellationToken ct)
    {
        var user = await _db.PlatformUsers
            .FirstOrDefaultAsync(u => u.Email == request.Email && u.IsActive, ct);

        if (user is not null && !string.IsNullOrEmpty(user.Email))
        {
            var ttlMinutes = _settings.PasswordResetTokenTtlMinutes;
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            await _cache.SetAsync($"{CacheKeyPrefix}{token}", new PasswordResetToken(user.Id),
                TimeSpan.FromMinutes(ttlMinutes), ct);

            await _email.SendAsync(
                user.Email,
                "Reset your ROCloud admin password",
                $"Use this token to reset your admin password (valid for {ttlMinutes} minutes): {token}",
                ct);
        }

        // Always succeed — never reveal whether the email exists.
    }
}
