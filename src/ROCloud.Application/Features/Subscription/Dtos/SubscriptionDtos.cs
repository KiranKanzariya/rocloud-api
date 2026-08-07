namespace ROCloud.Application.Features.Subscription.Dtos;

/// <summary>The tenant's current subscription, plan limits, and live usage (guide §25).</summary>
public sealed record SubscriptionDto(
    string PlanName,
    string PlanType,
    decimal MonthlyPrice,
    string Status,
    DateTime? TrialEndsAt,
    DateTime? SubscriptionEndsAt,
    UsageDto Usage,
    string SubscriptionDiscountType,
    decimal SubscriptionDiscountValue,
    decimal NetMonthlyPrice,
    /// <summary>A downgrade waiting for period end, so the owner can see it coming (and undo it by
    /// re-selecting their current plan). Null when nothing is pending.</summary>
    string? ScheduledPlanName = null,
    /// <summary>Plan type of <see cref="ScheduledPlanName"/>, for the UI to match against the plan list.</summary>
    string? ScheduledPlanType = null);

/// <summary>
/// Who the subscription invoice is billed to — the tenant's own business details, as they should
/// appear on the document. Sent only on the detail view; the history list would repeat the same
/// block on every row for no benefit.
/// </summary>
public sealed record SubscriptionBillToDto(
    string Name,
    string? Gstin,
    string? AddressLine,
    string? City,
    string? State,
    string? Pincode,
    string? Email,
    string? Mobile);

/// <summary>
/// A ROCloud subscription invoice row for the owner's Billing history (guide §25).
/// The trailing members are populated only by the detail query (see <see cref="SubscriptionBillToDto"/>);
/// they default to null so the list projection stays lean.
/// </summary>
public sealed record SubscriptionInvoiceDto(
    Guid Id,
    string InvoiceNumber,
    string PlanType,
    string BillingCycle,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal GrossAmount,
    decimal DiscountAmount,
    decimal Amount,
    string Status,
    DateOnly DueDate,
    string? Description,
    DateTime? PaidAt,
    string? RazorpayOrderId = null,
    string? RazorpayPaymentId = null,
    SubscriptionBillToDto? BillTo = null,
    /// <summary>How it was paid: card | upi | netbanking | wallet.</summary>
    string? PaymentMethod = null,
    /// <summary>Display detail for the method — UPI id, "Visa •••• 4366", bank, or wallet.</summary>
    string? PaymentInstrument = null,
    /// <summary>Why a Cancelled invoice was withdrawn — shown beneath it in the billing list, so the
    /// owner is never left comparing an emailed bill against a cancellation with no explanation.</summary>
    string? CancellationReason = null);

/// <summary>Usage counts vs the plan's limits.</summary>
public sealed record UsageDto(
    int Customers, int MaxCustomers,
    int Users, int MaxUsers,
    int DeliveryBoys, int MaxDeliveryBoys);

/// <summary>
/// Checkout parameters returned to the Angular client to open Razorpay for a plan change.
/// In dev (no live Razorpay key) DevMode=true and SubscriptionId is null — the client simulates
/// a successful payment and calls upgrade-complete directly.
/// </summary>
public sealed record SubscriptionInitiateDto(
    string KeyId,
    string? OrderId,
    string PlanType,
    decimal Amount,
    string Currency,
    bool DevMode,
    decimal GrossAmount,
    decimal DiscountAmount,
    /// <summary>True when the net amount is ≤ 0 (e.g. a 100% discount or free months). The client
    /// skips Razorpay entirely and completes the upgrade directly — Razorpay rejects ₹0 orders.</summary>
    bool IsFree,
    /// <summary>NewTerm | Upgrade | Downgrade | Lateral — what this change actually does, so the
    /// client can explain it before taking money (see PlanChangeCalculator).</summary>
    string ChangeKind = nameof(PlanChangeKind.NewTerm),
    /// <summary>Days left in the current cycle, which <see cref="Amount"/> was prorated over. 0 when
    /// there is no live term (a full cycle is being bought).</summary>
    int RemainingDays = 0,
    /// <summary>Full-cycle price of the target plan, before proration. Lets the UI say "₹541.94 now,
    /// then ₹2,499/month from 8 Aug" rather than showing a bare unexplained figure.</summary>
    decimal FullCycleAmount = 0m,
    /// <summary>When a scheduled downgrade takes effect — the current period end. Null otherwise.</summary>
    DateTime? EffectiveAt = null);
