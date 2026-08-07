using ROCloud.Domain.Entities.Common;

namespace ROCloud.Domain.Entities.Platform;

/// <summary>
/// A ROCloud subscription invoice raised against a tenant (the tenant's own plan bill, guide §25/§26).
/// Platform-owned (NOT tenant-scoped) — same ownership as <see cref="PlatformBillingTransaction"/>.
/// Lifecycle: <c>Pending</c> (owner must pay) → <c>Paid</c>, or <c>Cancelled</c> when the tenant's term
/// or plan moved underneath it — always with a <see cref="CancellationReason"/>, since a withdrawn bill
/// the owner has already been emailed needs to explain itself. On payment a
/// <see cref="PlatformBillingTransaction"/> is also written (the admin paid-ledger).
/// DB table: subscription_invoices.
/// </summary>
public class SubscriptionInvoice : BaseEntity
{
    public Guid TenantId { get; set; }

    /// <summary>Human-friendly, globally unique, e.g. <c>SUB-2026-000042</c>.</summary>
    public string InvoiceNumber { get; set; } = string.Empty;

    public string PlanType { get; set; } = string.Empty;      // Starter | Basic | Pro | Enterprise
    public string BillingCycle { get; set; } = "Monthly";     // Monthly | Yearly

    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }

    public decimal GrossAmount { get; set; }
    public decimal DiscountAmount { get; set; }

    /// <summary>Net payable = gross − discount (≥ 0).</summary>
    public decimal Amount { get; set; }

    public string Status { get; set; } = "Pending";           // Pending | Paid | Cancelled
    public DateOnly DueDate { get; set; }
    public string? Description { get; set; }

    /// <summary>
    /// Why this invoice was withdrawn, in a sentence the owner can read — shown on their billing page
    /// and stamped on the PDF. Null for every status other than Cancelled, and for rows cancelled
    /// before the reason was recorded.
    /// </summary>
    public string? CancellationReason { get; set; }

    public string? RazorpayOrderId { get; set; }
    public string? RazorpayPaymentId { get; set; }

    /// <summary>How it was paid — Razorpay's method: card | upi | netbanking | wallet.</summary>
    public string? PaymentMethod { get; set; }

    /// <summary>Display detail for <see cref="PaymentMethod"/>: the UPI id, "Visa •••• 4366", bank, or wallet.</summary>
    public string? PaymentInstrument { get; set; }
    public DateTime? PaidAt { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
}
