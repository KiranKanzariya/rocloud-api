namespace ROCloud.Application.Features.TenantSettings.Dtos;

/// <summary>The tenant's business profile / settings (guide §24). Backed by the tenants row.</summary>
public sealed record TenantSettingsDto(
    Guid Id,
    string Name,
    string Subdomain,
    string OwnerName,
    string OwnerEmail,
    string OwnerMobile,
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
    string PlanType,
    string Status,
    // Scan-to-pay UPI id shown as a QR on customer invoices/statements (§10).
    string? UpiVpa = null,
    string? UpiPayeeName = null,
    bool UpiQrEnabled = false,
    // Verification state for the CURRENT UpiVpa — cleared server-side whenever that id changes, so
    // these are never stale relative to the id beside them.
    DateTime? UpiVerifiedAt = null,
    string? UpiVerifiedName = null);

/// <summary>
/// Result of checking a UPI id against the payments network.
/// </summary>
/// <param name="Verified">The id exists. Not proof it belongs to this owner — see <paramref name="PayeeName"/>.</param>
/// <param name="PayeeName">The account name it is registered to, for the owner to recognise as their own.</param>
/// <param name="Unavailable">
/// The check could not be RUN (no payment credentials, network, or the endpoint is not enabled on the
/// merchant account). Deliberately distinct from "this id does not exist" — telling an owner their
/// working UPI id is invalid would be worse than admitting we could not check.
/// </param>
public sealed record UpiVerificationDto(
    string Vpa,
    bool Verified,
    string? PayeeName,
    bool Unavailable);
