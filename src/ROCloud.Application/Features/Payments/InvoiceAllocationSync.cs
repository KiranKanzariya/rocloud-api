using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Payments;

/// <summary>
/// Recomputes, and PERSISTS, what each of a customer's invoices has really been paid — including money
/// the owner recorded against the customer rather than against an invoice, spread oldest-obligation
/// first (guide §9).
///
/// WHY PERSIST rather than derive at read time: an invoice's status has to be filterable. The FIFO
/// spread is a running sum across the customer's whole ladder (invoices AND uninvoiced orders), which
/// cannot be expressed in a SQL WHERE clause — so a derived status meant "filter by Sent" could return
/// a row that renders "Paid", and "filter by Paid" missed genuinely settled invoices. Writing the
/// answer down makes the database the truth again, so every reader (list, filter, reminders) is simply
/// correct with no extra work.
///
/// Call this from EVERY path that changes a customer's payments or invoices — see the callers. Missing
/// one leaves a stale status behind; that is the price of materialising, and the tests guard each one.
///
/// It is a full recompute, so it is idempotent and it can also REDUCE a paid amount (e.g. a payment
/// that turned out to have failed). Order allocations stay derived at read time
/// (<see cref="CustomerObligationAllocator"/>) — the pool it sees is what is left after the invoice
/// amounts written here, so the two always agree.
/// </summary>
public static class InvoiceAllocationSync
{
    /// <summary>
    /// Recomputes one customer's invoice allocations AND persists them. Use from single-customer write
    /// paths (payment collected, delivery marked, opening balance set, one invoice generated).
    /// </summary>
    public static async Task SyncAsync(IAppDbContext db, Guid customerId, CancellationToken ct)
    {
        await SyncWithoutSaveAsync(db, customerId, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Recomputes one customer's invoice allocations, mutating the tracked <see cref="Domain.Entities.Tenant.Invoice"/>
    /// entities but NOT saving. Callers that process many customers in a loop (bulk invoice generation,
    /// payment reconciliation, the nightly re-settle job) should call this per customer and then
    /// <c>SaveChangesAsync</c> ONCE after the loop — one transaction instead of one per customer.
    ///
    /// All database reads happen up front, before any entity is mutated, so a read failure leaves the
    /// change tracker clean for this customer — a loop caller can catch it, skip the customer, and still
    /// safely save the customers that succeeded.
    /// </summary>
    public static async Task SyncWithoutSaveAsync(IAppDbContext db, Guid customerId, CancellationToken ct)
    {
        var invoices = await db.Invoices
            .Where(i => i.CustomerId == customerId && i.Status != InvoiceStatus.Cancelled)
            .ToListAsync(ct);

        if (invoices.Count == 0) return;

        var payments = await db.Payments
            .Where(p => p.CustomerId == customerId && p.Status == PaymentStatus.Completed)
            .Select(p => new { p.Amount, p.InvoiceId })
            .ToListAsync(ct);

        // Delivered orders not covered by an invoice period compete for the leftover pool (step 3). Read
        // it here, alongside the other reads and BEFORE any mutation below, so nothing is left half-applied
        // if a read throws. Its predicate reads only invoice existence/period/status (never PaidAmount),
        // and tracked mutations are not flushed until save, so ordering it first changes no result.
        var orders = await db.Orders
            .Where(o => o.CustomerId == customerId
                && o.Status == OrderStatus.Delivered
                && !db.Invoices.Any(inv => inv.CustomerId == customerId
                    && inv.Status != InvoiceStatus.Cancelled
                    && inv.PeriodFrom != null && inv.PeriodTo != null
                    && o.OrderDate >= inv.PeriodFrom && o.OrderDate <= inv.PeriodTo))
            .Select(o => new
            {
                o.Id,
                o.OrderDate,
                o.CreatedAt,
                Total = o.OrderItems.Sum(i => (decimal?)(i.Quantity * i.UnitRate)) ?? 0m
            })
            .ToListAsync(ct);

        // 1) Start from money booked directly against each invoice, capped at what it is worth. The
        //    surplus of an over-payment is deliberately left over, to be spent on the customer's other
        //    dues below rather than stranded on a settled row.
        foreach (var invoice in invoices)
        {
            var linked = payments.Where(p => p.InvoiceId == invoice.Id).Sum(p => p.Amount);
            invoice.PaidAmount = Math.Min(linked, invoice.TotalAmount);
        }

        // 2) Everything else the customer has paid is free to settle whatever they owe.
        var pool = Math.Max(0m, payments.Sum(p => p.Amount) - invoices.Sum(i => i.PaidAmount));

        // 3) Delivered orders not covered by an invoice period compete for that same pool, so an order
        //    older than an invoice is settled first (loaded above). Their share is NOT stored (orders
        //    carry no paid column) — it is recomputed identically at read time from the pool this leaves.
        var ladder = invoices
            .Select(i => (
                Date: i.InvoiceDate,
                i.CreatedAt,
                Outstanding: Math.Max(0m, i.TotalAmount - i.PaidAmount),
                Invoice: (Domain.Entities.Tenant.Invoice?)i))
            .Concat(orders.Select(o => (
                Date: o.OrderDate,
                o.CreatedAt,
                Outstanding: Math.Max(0m, o.Total),
                Invoice: (Domain.Entities.Tenant.Invoice?)null)))
            .OrderBy(x => x.Date).ThenBy(x => x.CreatedAt);

        foreach (var item in ladder)
        {
            var applied = Math.Min(item.Outstanding, pool);
            pool -= applied;
            if (item.Invoice is { } invoice) invoice.PaidAmount += applied;
        }

        // 4) Status follows the money. A Draft/Sent/Overdue invoice with nothing against it keeps the
        //    status it was given (SendInvoice owns that transition). But because this is a full
        //    recompute it must also DEMOTE: if the money behind a Paid invoice went away (a payment
        //    that failed, an opening balance cleared), it must stop claiming to be paid.
        foreach (var invoice in invoices)
        {
            invoice.Status = invoice.TotalAmount > 0m && invoice.PaidAmount >= invoice.TotalAmount
                ? InvoiceStatus.Paid
                : invoice.PaidAmount > 0m
                    ? InvoiceStatus.PartiallyPaid
                    : invoice.Status is InvoiceStatus.Paid or InvoiceStatus.PartiallyPaid
                        ? InvoiceStatus.Sent          // was paid, no longer is → owed again
                        : invoice.Status;
        }
    }
}
