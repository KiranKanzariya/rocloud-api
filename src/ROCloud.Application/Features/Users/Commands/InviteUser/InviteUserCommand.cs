using System.Security.Cryptography;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Security;
using ROCloud.Application.Common.Settings;

namespace ROCloud.Application.Features.Users.Commands.InviteUser;

/// <summary>
/// Invites a team member: provisions the account (same checks as CreateUser) but emails an
/// invitation link to the portal instead of raw credentials. WhatsApp invite is Phase 14.
/// </summary>
public sealed record InviteUserCommand(
    string Name,
    string Email,
    string? Mobile,
    Guid RoleId,
    IReadOnlyList<Guid>? AreaIds) : IRequest<Guid>;

public class InviteUserCommandValidator : AbstractValidator<InviteUserCommand>
{
    public InviteUserCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().Length(2, 200);
        RuleFor(c => c.Email).NotEmpty().EmailAddress().MaximumLength(200);
        RuleFor(c => c.Mobile)
            .Matches(@"^\+91[0-9]{10}$").When(c => !string.IsNullOrEmpty(c.Mobile))
            .WithMessage("Invalid mobile number.");
        RuleFor(c => c.RoleId).NotEmpty();
    }
}

public class InviteUserCommandHandler : IRequestHandler<InviteUserCommand, Guid>
{
    // How long an invitation link stays valid.
    private static readonly TimeSpan InviteTtl = TimeSpan.FromDays(7);

    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IPasswordService _passwords;
    private readonly IEmailService _email;
    private readonly ICacheService _cache;
    private readonly ILogger<InviteUserCommandHandler> _logger;
    private readonly IAppSettings _settings;

    public InviteUserCommandHandler(
        IAppDbContext db, ITenantContext tenant, IPasswordService passwords,
        IEmailService email, ICacheService cache, ILogger<InviteUserCommandHandler> logger, IAppSettings settings)
    {
        _db = db;
        _tenant = tenant;
        _passwords = passwords;
        _email = email;
        _cache = cache;
        _logger = logger;
        _settings = settings;
    }

    public async Task<Guid> Handle(InviteUserCommand request, CancellationToken ct)
    {
        var (user, _) = await UserProvisioning.CreateAsync(
            _db, _tenant, _passwords,
            request.Name, request.Mobile, request.Email, request.RoleId,
            preferredLanguage: null, request.AreaIds, ct);

        await _db.SaveChangesAsync(ct);

        // Emit a real, working invitation link: a password-reset token (consumed by the existing
        // /reset-password flow) pointed at the tenant's own portal. Previously the link had no token,
        // so the invited user could never set a password from it.
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await _cache.SetAsync($"pwreset:{token}", new PasswordResetToken(user.Id), InviteTtl, ct);

        var subdomain = await _db.Tenants
            .Where(t => t.Id == _tenant.TenantId)
            .Select(t => t.Subdomain)
            .FirstAsync(ct);
        var inviteUrl = $"{_settings.TenantUrlFormat.Replace("{subdomain}", subdomain)}/reset-password?token={token}";

        await _email.SendAsync(
            request.Email,
            "You've been invited to ROCloud",
            "You've been invited to join your team on ROCloud. " +
            $"<a href=\"{inviteUrl}\">Accept your invitation</a> to set your password. " +
            $"This link is valid for {InviteTtl.Days} days.", ct);

        // TODO (Phase 14 — WhatsApp): also send the invitation link via MSG91/WhatsApp.
        _logger.LogInformation("TODO[Phase14]: WhatsApp invite to {Mobile}", request.Mobile);

        return user.Id;
    }
}
