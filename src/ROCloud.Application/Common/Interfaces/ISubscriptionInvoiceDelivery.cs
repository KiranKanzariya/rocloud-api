using ROCloud.Domain.Entities.Platform;

namespace ROCloud.Application.Common.Interfaces;

/// <summary>
/// Renders + stores the subscription-invoice PDF and emails the owner (platform → tenant, ROCloud
/// branded). Best-effort: failures are logged, never thrown, so the billing transaction always
/// commits even if the PDF/email step fails. Sets <see cref="SubscriptionInvoice.PdfUrl"/> on the
/// passed entity (the caller persists it).
/// </summary>
public interface ISubscriptionInvoiceDelivery
{
    /// <summary>A newly issued Pending invoice: store the PDF and email the owner the "please pay" mail.</summary>
    Task IssueAsync(SubscriptionInvoice invoice, Tenant tenant, CancellationToken ct = default);

    /// <summary>A just-paid invoice: store the PAID PDF and email the owner a payment receipt.</summary>
    Task ReceiptAsync(SubscriptionInvoice invoice, Tenant tenant, CancellationToken ct = default);

    /// <summary>Render + store the PDF only (sets <see cref="SubscriptionInvoice.PdfUrl"/>), no email —
    /// used for ₹0 auto-renewals so the invoice is still downloadable/previewable.</summary>
    Task StorePdfAsync(SubscriptionInvoice invoice, Tenant tenant, CancellationToken ct = default);
}
