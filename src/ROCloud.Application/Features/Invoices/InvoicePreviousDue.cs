using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Customers;

namespace ROCloud.Application.Features.Invoices;

/// <summary>
/// The "Previous Due" snapshot written onto a new invoice: what the customer owed on EVERYTHING ELSE
/// at the moment it was raised.
///
/// <para>
/// <see cref="CustomerBalance"/> counts delivered orders that no invoice covers yet — which includes the
/// very orders this invoice is about to bill. Subtracting the period's own subtotal removes them, leaving
/// the older invoices and any uninvoiced orders outside the period. Using the canonical balance rather
/// than a fresh definition of "due" is deliberate: a third definition would drift from the dues report
/// and the reminders that chase it.
/// </para>
///
/// <para>
/// Clamped at zero, and that is a correctness rule rather than cosmetics. When the customer holds an
/// advance the balance is negative, but <c>InvoiceAllocationSync</c> already spends that credit against
/// this invoice's <c>PaidAmount</c>. Storing the negative too would deduct the same credit twice —
/// once here and once there — and the invoice would ask for less than it should.
/// </para>
/// </summary>
internal static class InvoicePreviousDue
{
    public static async Task<decimal> ComputeAsync(
        IAppDbContext db, Guid customerId, decimal periodSubTotal, CancellationToken ct)
    {
        var balance = await CustomerBalance.ComputeAsync(db, customerId, ct);
        return Math.Max(0m, balance - periodSubTotal);
    }
}
