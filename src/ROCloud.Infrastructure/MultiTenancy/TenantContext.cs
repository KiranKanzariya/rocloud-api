using ROCloud.Application.Common.Interfaces;

namespace ROCloud.Infrastructure.MultiTenancy;

/// <summary>
/// Scoped, per-request implementation of <see cref="ITenantContext"/>.
/// Populated by TenantMiddleware (Phase 4/6). Defaults are safe for design-time
/// (migrations) and unauthenticated requests.
/// </summary>
public class TenantContext : ITenantContext
{
    public Guid TenantId { get; set; } = Guid.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string PlanType { get; set; } = string.Empty;
    public string LanguageCode { get; set; } = "en";
}
