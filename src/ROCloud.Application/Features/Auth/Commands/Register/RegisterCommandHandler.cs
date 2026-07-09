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

namespace ROCloud.Application.Features.Auth.Commands.Register;

public class RegisterCommandHandler : IRequestHandler<RegisterCommand, AuthResult>
{
    private readonly IAppDbContext _db;
    private readonly IPasswordService _passwords;
    private readonly TenantProvisioner _provisioner;
    private readonly AuthTokenIssuer _issuer;
    private readonly IEmailService _email;
    private readonly IAppSettings _settings;

    public RegisterCommandHandler(
        IAppDbContext db, IPasswordService passwords, TenantProvisioner provisioner,
        AuthTokenIssuer issuer, IEmailService email, IAppSettings settings)
    {
        _db = db;
        _passwords = passwords;
        _provisioner = provisioner;
        _issuer = issuer;
        _email = email;
        _settings = settings;
    }

    public async Task<AuthResult> Handle(RegisterCommand request, CancellationToken ct)
    {
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

        // One business per email: block re-registering a workspace with an email that already owns one.
        var email = request.Email.Trim();
        var emailOwnsTenant = await _db.Tenants.IgnoreQueryFilters()
            .AnyAsync(t => t.OwnerEmail.ToLower() == email.ToLower() && !t.IsDeleted, ct);
        if (emailOwnsTenant)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["email"] = ["An account already exists for this email. Please sign in instead."]
            });

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            PlanId = plan.Id,
            Plan = plan,
            Name = request.BusinessName,
            Subdomain = subdomain,
            OwnerName = request.OwnerName,
            OwnerEmail = request.Email,
            OwnerMobile = request.Mobile,
            Status = TenantStatus.Trial,
            TrialEndsAt = DateTime.UtcNow.AddDays(_settings.TrialDays),
            DefaultLanguage = "en"
        };
        _db.Tenants.Add(tenant);

        var passwordHash = _passwords.Hash(request.Password);
        var provision = await _provisioner.ProvisionAsync(
            tenant, request.OwnerName, request.Email, passwordHash, request.Mobile, ct);

        // AuthTokenIssuer.IssueAsync persists the whole tracked graph (tenant, roles, owner, products).
        var result = await _issuer.IssueAsync(provision.Owner, tenant, provision.OwnerPermissions, ct);

        // Tell the owner exactly where to sign in — their tenant has its own subdomain URL.
        var loginUrl = _settings.TenantUrlFormat.Replace("{subdomain}", subdomain);
        await _email.SendAsync(
            request.Email,
            "Welcome to ROCloud",
            $"Hello {request.OwnerName}, welcome to ROCloud!\n\n" +
            $"Your portal is ready at: {loginUrl}\n" +
            $"Sign in there any time with this email address. Bookmark the link so you can find it again.\n\n" +
            $"Your free trial runs until {tenant.TrialEndsAt:d MMM yyyy}.",
            ct);

        return result;
    }
}
