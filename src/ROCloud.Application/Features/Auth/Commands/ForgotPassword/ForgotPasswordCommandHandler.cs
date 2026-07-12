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
    private readonly INotificationTemplateRenderer _templates;

    public ForgotPasswordCommandHandler(
        IAppDbContext db, ICacheService cache, IEmailService email, IAppSettings settings,
        INotificationTemplateRenderer templates)
    {
        _db = db;
        _cache = cache;
        _email = email;
        _settings = settings;
        _templates = templates;
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

            // Send a clickable link to the portal's reset page — it reads the token from ?token=.
            // A raw token alone leaves the owner with nothing actionable. `tenant` is non-null here
            // (a user is only resolved once the tenant was).
            var resetUrl =
                $"{_settings.TenantUrlFormat.Replace("{subdomain}", tenant!.Subdomain)}/reset-password?token={token}";

            var tokens = new Dictionary<string, string>
            {
                ["Name"] = user.Name,
                ["ResetUrl"] = resetUrl,
                ["Minutes"] = ttlMinutes.ToString(),
            };
            // Platform mail — render the shared default (tenant_id NULL) in the tenant's language.
            var rendered = await _templates.RenderAsync(
                null, "password_reset", tenant.DefaultLanguage, "Email", tokens, ct);

            await _email.SendAsync(
                user.Email,
                rendered?.Subject ?? "Reset your ROCloud password",
                rendered?.Body ??
                    $"Hi {user.Name}, we received a request to reset the password for your ROCloud account.\n\n" +
                    $"<a href=\"{resetUrl}\">Reset your password</a>\n\n" +
                    $"This link is valid for {ttlMinutes} minutes and can be used once.\n\n" +
                    $"If you didn't request this, you can safely ignore this email — your password will not change.",
                ct);
        }

        // Always succeed — never reveal whether the email exists.
    }
}
