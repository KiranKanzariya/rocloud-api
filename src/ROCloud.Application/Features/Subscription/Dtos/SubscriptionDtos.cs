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
    decimal NetMonthlyPrice);

/// <summary>A ROCloud subscription invoice row for the owner's Billing history (guide §25).</summary>
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
    DateTime? PaidAt);

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
    bool IsFree);
