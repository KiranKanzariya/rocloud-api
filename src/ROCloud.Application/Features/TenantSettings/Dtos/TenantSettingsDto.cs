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
    string Status);
