namespace ROCloud.Application.Features.TenantSettings.Dtos;

/// <summary>
/// The GST configuration that appears on an invoice. Split out of <see cref="TenantSettingsDto"/> so
/// invoicing roles can read it without also receiving the owner's contact details, plan and billing
/// status. GstPercent is a percentage (18), not a rate (0.18).
/// </summary>
public sealed record BillingSettingsDto(bool GstEnabled, decimal GstPercent, string? GstNumber);
