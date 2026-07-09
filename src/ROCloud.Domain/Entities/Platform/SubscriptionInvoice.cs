using ROCloud.Domain.Entities.Common;

namespace ROCloud.Domain.Entities.Platform;

/// <summary>
/// A ROCloud subscription invoice raised against a tenant (the tenant's own plan bill, guide §25/§26).
/// Platform-owned (NOT tenant-scoped) — same ownership as <see cref="PlatformBillingTransaction"/>.
/// Lifecycle: <c>Pending</c> (owner must pay) → <c>Paid</c>, or <c>Void</c> when superseded by an
/// upgrade/renewal that already covered the period. On payment a <see cref="PlatformBillingTransaction"/>
/// is also written (the admin paid-ledger). DB table: subscription_invoices.
/// </summary>
public class SubscriptionInvoice : BaseEntity
{
    public Guid TenantId { get; set; }

    /// <summary>Human-friendly, globally unique, e.g. <c>SUB-2026-000042</c>.</summary>
    public string InvoiceNumber { get; set; } = string.Empty;

    public string PlanType { get; set; } = string.Empty;      // Basic | Pro | Enterprise
    public string BillingCycle { get; set; } = "Monthly";     // Monthly | Yearly

    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }

    public decimal GrossAmount { get; set; }
    public decimal DiscountAmount { get; set; }

    /// <summary>Net payable = gross − discount (≥ 0).</summary>
    public decimal Amount { get; set; }

    public string Status { get; set; } = "Pending";           // Pending | Paid | Void
    public DateOnly DueDate { get; set; }
    public string? Description { get; set; }

    public string? RazorpayOrderId { get; set; }
    public string? RazorpayPaymentId { get; set; }
    public DateTime? PaidAt { get; set; }

    /// <summary>Stored PDF path (via IFileStorage, folder "subscription-invoices"). Set in Stage 2.</summary>
    public string? PdfUrl { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
}
