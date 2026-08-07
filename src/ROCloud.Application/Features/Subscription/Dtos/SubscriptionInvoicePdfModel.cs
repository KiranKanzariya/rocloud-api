namespace ROCloud.Application.Features.Subscription.Dtos;

/// <summary>
/// The data for a ROCloud subscription-invoice PDF: seller = ROCloud, buyer = the tenant. Simple
/// net-amount layout for v1 (no GST split — decision §11.5). <see cref="Paid"/> stamps the document.
///
/// <para><see cref="Cancelled"/> outranks it: a withdrawn bill must say so on its face, because the
/// owner already has the original in their inbox and will otherwise read it as still owing.
/// <see cref="CancellationReason"/> carries the sentence that explains which action withdrew it.</para>
/// </summary>
public sealed record SubscriptionInvoicePdfModel(
    string InvoiceNumber,
    DateOnly InvoiceDate,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string PlanType,
    string BillingCycle,
    string LineDescription,
    decimal GrossAmount,
    decimal DiscountAmount,
    decimal Amount,
    bool Paid,
    string TenantName,
    string? TenantGstin,
    bool Cancelled = false,
    string? CancellationReason = null);
