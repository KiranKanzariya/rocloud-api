using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Settings;
using ROCloud.Application.Features.Auth.Common;
using ROCloud.Application.Features.Auth.Services;
using ROCloud.Domain.Entities.Platform;
using ROCloud.Domain.Enums;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Features.Auth.Commands.RegisterGoogle;

/// <summary>
/// Creates a brand-new tenant whose Owner authenticates with Google (no password). Mirrors
/// <c>RegisterCommandHandler</c> but takes the owner identity from the verified Google id-token and
/// provisions the Owner with <see cref="AuthProvider.Google"/>.
/// </summary>
public class RegisterGoogleCommandHandler : IRequestHandler<RegisterGoogleCommand, AuthResult>
{
    private readonly IAppDbContext _db;
    private readonly IGoogleAuthService _google;
    private readonly TenantProvisioner _provisioner;
    private readonly AuthTokenIssuer _issuer;
    private readonly IEmailService _email;
    private readonly IAppSettings _settings;
    private readonly INotificationTemplateRenderer _templates;

    public RegisterGoogleCommandHandler(
        IAppDbContext db, IGoogleAuthService google, TenantProvisioner provisioner,
        AuthTokenIssuer issuer, IEmailService email, IAppSettings settings,
        INotificationTemplateRenderer templates)
    {
        _db = db;
        _google = google;
        _provisioner = provisioner;
        _issuer = issuer;
        _email = email;
        _settings = settings;
        _templates = templates;
    }

    public async Task<AuthResult> Handle(RegisterGoogleCommand request, CancellationToken ct)
    {
        var info = await _google.ValidateAsync(request.IdToken, ct);
        if (info is null)
            throw new InvalidCredentialsException();

        var planType = Enum.Parse<PlanType>(request.PlanType);
        var plan = await _db.Plans.FirstOrDefaultAsync(p => p.PlanType == planType && p.IsActive, ct)
                   ?? throw new NotFoundException("Plan", request.PlanType);

        var subdomain = SubdomainSlug.From(string.IsNullOrWhiteSpace(request.Subdomain) ? request.BusinessName : request.Subdomain);
        if (string.IsNullOrEmpty(subdomain))
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["subdomain"] = ["Could not derive a valid subdomain from the business name."]
            });

        var taken = await _db.Tenants.IgnoreQueryFilters()
            .AnyAsync(t => t.Subdomain == subdomain && !t.IsDeleted, ct);
        if (taken)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["subdomain"] = [$"The subdomain '{subdomain}' is already taken."]
            });

        // One business per email: a Google account that already owns a workspace should sign in instead.
        var emailOwnsTenant = await _db.Tenants.IgnoreQueryFilters()
            .AnyAsync(t => t.OwnerEmail.ToLower() == info.Email.ToLower() && !t.IsDeleted, ct);
        if (emailOwnsTenant)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["email"] = ["An account already exists for this Google email. Please sign in instead."]
            });

        var mobile = request.Mobile ?? string.Empty;
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            PlanId = plan.Id,
            Plan = plan,
            Name = request.BusinessName,
            Subdomain = subdomain,
            OwnerName = info.Name,
            OwnerEmail = info.Email,
            OwnerMobile = mobile,
            Status = TenantStatus.Trial,
            TrialEndsAt = DateTime.UtcNow.AddDays(_settings.TrialDays),
            DefaultLanguage = "en"
        };
        _db.Tenants.Add(tenant);

        // Provision the standard roles + Owner, then convert the Owner to a passwordless Google account.
        var provision = await _provisioner.ProvisionAsync(tenant, info.Name, info.Email, string.Empty, mobile, ct);
        provision.Owner.PasswordHash = null;
        provision.Owner.AuthProvider = AuthProvider.Google;
        provision.Owner.GoogleId = info.Subject;
        provision.Owner.GoogleEmail = info.Email;
        provision.Owner.AvatarUrl = info.Picture;

        var result = await _issuer.IssueAsync(provision.Owner, tenant, provision.OwnerPermissions, ct);

        var loginUrl = _settings.TenantUrlFormat.Replace("{subdomain}", subdomain);
        var tokens = new Dictionary<string, string>
        {
            ["OwnerName"] = info.Name,
            ["LoginUrl"] = loginUrl,
            ["TrialEndsAt"] = tenant.TrialEndsAt?.ToString("d MMM yyyy") ?? string.Empty,
        };
        var rendered = await _templates.RenderAsync(
            null, "welcome_google", tenant.DefaultLanguage, "Email", tokens, ct);

        await _email.SendAsync(
            info.Email,
            rendered?.Subject ?? "Welcome to ROCloud",
            rendered?.Body ??
                $"Hello {info.Name}, welcome to ROCloud!\n\n" +
                $"Your portal is ready at: {loginUrl}\n" +
                $"Sign in there any time with Google using this email address. Bookmark the link so you can find it again.\n\n" +
                $"Your free trial runs until {tenant.TrialEndsAt:d MMM yyyy}.",
            ct);

        return result;
    }
}
