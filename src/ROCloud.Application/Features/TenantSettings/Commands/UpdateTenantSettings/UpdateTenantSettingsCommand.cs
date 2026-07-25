using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Features.TenantSettings.Commands.UpdateTenantSettings;

/// <summary>
/// Updates the tenant's business profile (name, GST, address, branding, default language).
/// Subdomain and owner identity are intentionally NOT editable here.
/// </summary>
public sealed record UpdateTenantSettingsCommand(
    string Name,
    string? GstNumber,
    bool GstEnabled,
    decimal GstPercent,
    string? AddressLine,
    string? City,
    string? State,
    string? Pincode,
    string? LogoUrl,
    string? PrimaryColor,
    string DefaultLanguage) : IRequest;

public class UpdateTenantSettingsCommandValidator : AbstractValidator<UpdateTenantSettingsCommand>
{
    public UpdateTenantSettingsCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().Length(2, 200);
        RuleFor(c => c.GstNumber)
            .Matches(@"^[0-9A-Z]{15}$").When(c => !string.IsNullOrEmpty(c.GstNumber))
            .WithMessage("GST number must be 15 characters (digits/uppercase letters).");
        RuleFor(c => c.GstPercent).InclusiveBetween(0m, 100m)
            .WithMessage("GST rate must be between 0 and 100 percent.");
        RuleFor(c => c.AddressLine).MaximumLength(500);
        RuleFor(c => c.City).MaximumLength(100);
        RuleFor(c => c.State).MaximumLength(100);
        RuleFor(c => c.Pincode)
            .Matches(@"^[0-9]{6}$").When(c => !string.IsNullOrEmpty(c.Pincode))
            .WithMessage("Pincode must be 6 digits.");
        RuleFor(c => c.PrimaryColor)
            .Matches(@"^#[0-9a-fA-F]{6}$").When(c => !string.IsNullOrEmpty(c.PrimaryColor))
            .WithMessage("Primary colour must be a hex value like #0C447C.");
        RuleFor(c => c.LogoUrl).MaximumLength(500);
        RuleFor(c => c.DefaultLanguage).NotEmpty().MaximumLength(5);
    }
}

public class UpdateTenantSettingsCommandHandler : IRequestHandler<UpdateTenantSettingsCommand>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;

    public UpdateTenantSettingsCommandHandler(IAppDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task Handle(UpdateTenantSettingsCommand request, CancellationToken ct)
    {
        var t = await _db.Tenants.FirstOrDefaultAsync(x => x.Id == _tenant.TenantId, ct)
                ?? throw new NotFoundException("Tenant", _tenant.TenantId);

        // You cannot legally charge GST without a registration number, so GST must not be TURNED ON
        // without a GstNumber — otherwise the tenant issues a "tax invoice" with no GSTIN, which is an
        // improper document. Guarded on the OFF→ON transition only: a tenant already in this state (from
        // before this rule) is not blocked from saving unrelated fields; they clear it by adding a GSTIN
        // or turning GST off. Run audit-gst-enabled-without-gstin.sql to find any such tenants.
        var enablingGst = request.GstEnabled && !t.GstEnabled;
        if (enablingGst && string.IsNullOrWhiteSpace(request.GstNumber))
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["gstNumber"] = ["Enter your 15-digit GSTIN to charge GST, or keep GST off."]
            });

        t.Name = request.Name;
        t.GstNumber = string.IsNullOrWhiteSpace(request.GstNumber) ? null : request.GstNumber;
        t.GstEnabled = request.GstEnabled;
        t.GstRate = Math.Round(request.GstPercent / 100m, 4);
        t.AddressLine = request.AddressLine;
        t.City = request.City;
        t.State = request.State;
        t.Pincode = string.IsNullOrWhiteSpace(request.Pincode) ? null : request.Pincode;
        t.LogoUrl = request.LogoUrl;
        t.PrimaryColor = string.IsNullOrWhiteSpace(request.PrimaryColor) ? null : request.PrimaryColor;
        t.DefaultLanguage = request.DefaultLanguage;

        await _db.SaveChangesAsync(ct);
    }
}
