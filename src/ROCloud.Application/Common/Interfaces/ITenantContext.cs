namespace ROCloud.Application.Common.Interfaces;

/// <summary>
/// Per-request tenant context, populated by TenantMiddleware (Phase 4/6).
/// Registered as a scoped service.
/// </summary>
public interface ITenantContext
{
    Guid TenantId { get; set; }
    string Subdomain { get; set; }
    string PlanType { get; set; }

    /// <summary>Resolved request language (BCP-47 code, e.g. "en", "hi") for localization.</summary>
    string LanguageCode { get; set; }
}
