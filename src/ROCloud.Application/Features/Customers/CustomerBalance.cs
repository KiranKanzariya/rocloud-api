using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Customers;

/// <summary>
/// The canonical customer ledger (guide §9): everything billed minus everything paid. The result is
/// SIGNED — a positive value is money owed, a negative value is an advance/credit the customer holds
/// (e.g. they paid ₹100 against a ₹50 due). Callers decide how to present due vs. credit.
///
///   Billed = Σ non-cancelled invoices (gross TotalAmount)
///          + Σ delivered orders NOT covered by any invoice's [PeriodFrom, PeriodTo]
///            (invoices carry no per-order link, so coverage is by date range)
///   Paid   = Σ every Completed payment for the customer (doorstep / invoice / advance)
///
/// Counting all payments — not just order-linked ones — is what lets a single lump-sum payment
/// clear several past orders, lets a standalone advance reduce the balance, and surfaces credit.
///
/// The customer-LIST query (<c>GetCustomersQuery</c>) mirrors this inline as set-based subqueries
/// for paging; keep the two in sync.
/// </summary>
public static class CustomerBalance
{
    public static async Task<decimal> ComputeAsync(IAppDbContext db, Guid customerId, CancellationToken ct)
    {
        var owedInvoices = await db.Invoices
            .Where(i => i.CustomerId == customerId && i.Status != InvoiceStatus.Cancelled)
            .SumAsync(i => (decimal?)i.TotalAmount, ct) ?? 0m;

        var owedUninvoicedOrders = await db.OrderItems
            .Where(oi => oi.Order!.CustomerId == customerId
                && oi.Order.Status == OrderStatus.Delivered
                && !db.Invoices.Any(inv => inv.CustomerId == customerId
                    && inv.Status != InvoiceStatus.Cancelled
                    && inv.PeriodFrom != null && inv.PeriodTo != null
                    && oi.Order.OrderDate >= inv.PeriodFrom && oi.Order.OrderDate <= inv.PeriodTo))
            .SumAsync(oi => (decimal?)(oi.Quantity * oi.UnitRate), ct) ?? 0m;

        var paid = await db.Payments
            .Where(p => p.CustomerId == customerId && p.Status == PaymentStatus.Completed)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;

        // Signed: > 0 owed, < 0 advance/credit held by the customer.
        return owedInvoices + owedUninvoicedOrders - paid;
    }
}
