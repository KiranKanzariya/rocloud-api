using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Invoices;

/// <summary>
/// Derives what an invoice is really worth to the owner: its recorded PaidAmount (payments linked
/// straight to it) PLUS its FIFO share of the customer's unallocated payment pool (see
/// <see cref="Payments.CustomerObligationAllocator"/>). The mirror of
/// <see cref="Orders.OrderPaymentStatus"/>, which has always done this for orders.
///
/// Without it, an owner who records a lump sum on the customer page settles the customer's BALANCE
/// but leaves every invoice reading "Sent" for ever — and the payment reminder keeps chasing money
/// that is already in the till.
/// </summary>
public static class InvoicePaymentStatus
{
    /// <summary>
    /// The (paid, balance, status) shown for an invoice. A cancelled invoice is owed nothing and
    /// never resurrects; otherwise the pool-allocated amount tops up whatever is recorded on the row.
    /// </summary>
    public static (decimal PaidAmount, decimal Balance, InvoiceStatus Status) Resolve(
        InvoiceStatus status, decimal totalAmount, decimal recordedPaid, decimal allocated)
    {
        if (status == InvoiceStatus.Cancelled)
            return (recordedPaid, 0m, status);

        var paid = recordedPaid + allocated;
        var balance = Math.Max(0m, totalAmount - paid);

        // Only ever moves an invoice TOWARDS paid — a Draft/Sent/Overdue with nothing against it keeps
        // the status the row was given.
        var effective = totalAmount > 0m && paid >= totalAmount
            ? InvoiceStatus.Paid
            : paid > 0m
                ? InvoiceStatus.PartiallyPaid
                : status;

        return (paid, balance, effective);
    }
}
