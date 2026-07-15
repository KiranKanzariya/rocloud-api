using ROCloud.Domain.Entities.Platform;

namespace ROCloud.Application.Common.Interfaces;

/// <summary>
/// Renders the subscription-invoice PDF and emails it to the owner (platform → tenant, ROCloud
/// branded). Best-effort: failures are logged, never thrown, so the billing transaction always commits
/// even if the PDF/email step fails. The PDF is only an email attachment — it is never stored, and the
/// download endpoint re-renders it from the invoice row.
/// </summary>
public interface ISubscriptionInvoiceDelivery
{
    /// <summary>A newly issued Pending invoice: email the owner the "please pay" mail with the PDF attached.</summary>
    Task IssueAsync(SubscriptionInvoice invoice, Tenant tenant, CancellationToken ct = default);

    /// <summary>A just-paid invoice: email the owner a payment receipt with the PAID PDF attached.</summary>
    Task ReceiptAsync(SubscriptionInvoice invoice, Tenant tenant, CancellationToken ct = default);
}
