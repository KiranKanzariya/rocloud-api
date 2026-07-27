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

    /// <summary>How it was paid — Razorpay's method: card | upi | netbanking | wallet.</summary>
    public string? PaymentMethod { get; set; }

    /// <summary>Display detail for <see cref="PaymentMethod"/>: the UPI id, "Visa •••• 4366", bank, or wallet.</summary>
    public string? PaymentInstrument { get; set; }

    /// <summary>
    /// The subscription invoice this charge paid for (SUB-…). Lets the admin billing detail open the
    /// actual invoice document. Nullable: legacy rows predate the link, and a free (₹0) upgrade may not
    /// have one. Set at creation in PayInvoiceComplete / CompleteUpgrade.
    /// </summary>
    public Guid? SubscriptionInvoiceId { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
    public SubscriptionInvoice? SubscriptionInvoice { get; set; }
}
