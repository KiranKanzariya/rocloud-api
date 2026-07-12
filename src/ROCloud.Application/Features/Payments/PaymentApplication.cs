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
        if (invoice.Status == InvoiceStatus.Cancelled)
            return;   // never resurrect a cancelled invoice; the money stays in the customer's pool

        // Never book more than the invoice still owes. The surplus is deliberately left unclaimed so
        // CustomerObligationAllocator — whose pool is (payments − PaidAmount) — can spend it on the
        // customer's other dues instead of it vanishing into an over-paid row.
        var owed = Math.Max(0m, invoice.TotalAmount - invoice.PaidAmount);
        invoice.PaidAmount += Math.Min(amount, owed);

        invoice.Status = invoice.TotalAmount > 0m && invoice.PaidAmount >= invoice.TotalAmount
            ? InvoiceStatus.Paid
            : invoice.PaidAmount > 0
                ? InvoiceStatus.PartiallyPaid
                : invoice.Status;
    }
}
