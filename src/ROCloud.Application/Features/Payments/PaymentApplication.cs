using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Payments;

/// <summary>
/// Shared logic for applying a completed payment to an invoice and recomputing its status.
/// Used by both CollectPayment (manual) and ConfirmRazorpayPayment (webhook).
/// </summary>
internal static class PaymentApplication
{
    public static void ApplyToInvoice(Invoice invoice, decimal amount)
    {
        invoice.PaidAmount += amount;

        if (invoice.Status == InvoiceStatus.Cancelled)
            return;   // never resurrect a cancelled invoice

        invoice.Status = invoice.PaidAmount >= invoice.TotalAmount
            ? InvoiceStatus.Paid
            : invoice.PaidAmount > 0
                ? InvoiceStatus.PartiallyPaid
                : invoice.Status;
    }
}
