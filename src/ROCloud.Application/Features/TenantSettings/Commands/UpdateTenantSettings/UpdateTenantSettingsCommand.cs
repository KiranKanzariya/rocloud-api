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
    string DefaultLanguage,
    string? UpiVpa = null,
    string? UpiPayeeName = null,
    bool UpiQrEnabled = false) : IRequest;

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
        // VPA shape only — "user@handle". Nothing can check the id actually exists or belongs to this
        // tenant, so this stops typos, not mistakes; the customer verifies against the id printed on
        // the invoice. Deliberately permissive on the handle (banks/PSPs keep adding new ones).
        RuleFor(c => c.UpiVpa)
            .Matches(@"^[a-zA-Z0-9.\-_]{2,64}@[a-zA-Z][a-zA-Z0-9.\-]{1,63}$")
            .When(c => !string.IsNullOrWhiteSpace(c.UpiVpa))
            .WithMessage("Enter a valid UPI ID, for example name@okaxis.");
        RuleFor(c => c.UpiPayeeName).MaximumLength(100);
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

        // Mirror of the GST rule: the QR cannot be TURNED ON without an id to pay into, or the invoice
        // ships a scan-to-pay block that resolves to nothing. Guarded on the OFF→ON transition only, so
        // a tenant already in that state can still save unrelated fields.
        var enablingUpi = request.UpiQrEnabled && !t.UpiQrEnabled;
        if (enablingUpi && string.IsNullOrWhiteSpace(request.UpiVpa))
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["upiVpa"] = ["Enter your UPI ID to show a payment QR, or keep the QR off."]
            });

        // Verification is deliberately NOT required here. Razorpay's Validate VPA API was withdrawn
        // with the NPCI UPI-Collect deprecation (28 Feb 2026), so no automated check can succeed for
        // any tenant — requiring one would make the QR permanently unreachable. The residual risk is a
        // mistyped id that happens to be a stranger's real UPI id, which pays the wrong account
        // silently; the settings screen warns about it and the id is printed beside the QR so the
        // owner and their customers can read it.
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
        var newVpa = string.IsNullOrWhiteSpace(request.UpiVpa) ? null : request.UpiVpa.Trim();

        // A changed id has NOT been checked, whatever the old one's result was. Leaving the tick and
        // the registered name in place would vouch for an id nobody has verified — the one thing the
        // verify button exists to prevent.
        if (!string.Equals(t.UpiVpa, newVpa, StringComparison.OrdinalIgnoreCase))
        {
            t.UpiVerifiedAt = null;
            t.UpiVerifiedName = null;
        }

        t.UpiVpa = newVpa;
        t.UpiPayeeName = string.IsNullOrWhiteSpace(request.UpiPayeeName) ? null : request.UpiPayeeName.Trim();
        // Clearing the id also switches the QR off — never leave the flag on with nothing behind it.
        t.UpiQrEnabled = request.UpiQrEnabled && t.UpiVpa is not null;

        await _db.SaveChangesAsync(ct);
    }
}
