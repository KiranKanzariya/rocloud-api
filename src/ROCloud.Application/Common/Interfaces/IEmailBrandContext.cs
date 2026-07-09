namespace ROCloud.Application.Common.Interfaces;

/// <summary>
/// The brand used to theme outgoing email for the current scope. Defaults to the ROCloud brand
/// (owner/platform mail); a customer-facing sender — e.g. the invoice email — sets it to the tenant's
/// business for that send, and the BrandedEmailService decorator reads it when wrapping. Scoped, so
/// each request/job scope starts from the ROCloud default.
/// </summary>
public interface IEmailBrandContext
{
    EmailBrand Current { get; set; }
}

/// <summary>Branding shown in the email header. Phase 2a uses the display name only; LogoUrl is reserved.</summary>
public sealed record EmailBrand(string DisplayName, string? LogoUrl = null)
{
    /// <summary>The platform default, used for owner/platform emails.</summary>
    public static readonly EmailBrand RoCloud = new("ROCloud");
}
