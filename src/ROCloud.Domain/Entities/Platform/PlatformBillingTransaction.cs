using ROCloud.Domain.Entities.Common;

namespace ROCloud.Domain.Entities.Platform;

/// <summary>
/// A ROCloud platform billing record — a tenant's subscription charge (guide §26 Billing).
/// Written when a tenant completes an upgrade. Not tenant-scoped (platform-owned); the platform
/// admin portal reads across all tenants. DB table: platform_billing_transactions.
/// </summary>
public class PlatformBillingTransaction : BaseEntity
{
    public Guid TenantId { get; set; }
    public string PlanType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string BillingCycle { get; set; } = "Monthly"; // Monthly | Yearly
    public string Status { get; set; } = "Paid";          // Paid | Failed | Refunded | Pending
    public string? RazorpayPaymentId { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
}
