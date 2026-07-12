using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Payments;

/// <summary>
/// FIFO-spreads each customer's unallocated payment pool across everything they owe — open invoices
/// AND delivered, uninvoiced orders — oldest obligation first (guide §9). One ladder for both kinds,
/// so a lump sum settles an old opening-balance invoice before it touches this week's deliveries, and
/// the same rupee can never be claimed by two obligations.
///
/// Pool = every Completed payment MINUS what is already committed to invoices (their PaidAmount).
/// Defining it that way — rather than "payments with no invoice_id" — means money overpaid onto one
/// invoice, or stranded on a cancelled one, flows back into the pool on its own. It relies on
/// PaidAmount never exceeding TotalAmount, which <see cref="PaymentApplication.ApplyToInvoice"/> caps.
///
/// By construction, (Σ obligations − pool) == <see cref="Customers.CustomerBalance"/>. Keep it so.
///
/// Nothing here reads PaymentPreference: PerBottle/Weekly/Combined customers owe orders, Monthly
/// customers owe invoices, and any customer can owe both (e.g. an imported opening balance).
/// </summary>
public static class CustomerObligationAllocator
{
    /// <summary>One thing a customer owes — an invoice or an order — and when it arose.</summary>
    private sealed record Obligation(Guid Id, bool IsInvoice, DateOnly Date, DateTime CreatedAt, decimal Outstanding);

    /// <summary>FIFO-applied amounts, keyed by invoice id and by order id.</summary>
    public sealed record Result(Dictionary<Guid, decimal> Invoices, Dictionary<Guid, decimal> Orders)
    {
        public static Result Empty() => new(new Dictionary<Guid, decimal>(), new Dictionary<Guid, decimal>());
    }

    /// <summary>
    /// Computes the FIFO-applied amount for every open invoice and every delivered, uninvoiced order
    /// of the given customers.
    /// </summary>
    public static async Task<Result> ComputeAsync(
        IAppDbContext db, IReadOnlyCollection<Guid> customerIds, CancellationToken ct)
    {
        if (customerIds.Count == 0) return Result.Empty();

        var paid = await db.Payments
            .Where(p => customerIds.Contains(p.CustomerId) && p.Status == PaymentStatus.Completed)
            .GroupBy(p => p.CustomerId)
            .Select(g => new { CustomerId = g.Key, Total = g.Sum(x => (decimal?)x.Amount) ?? 0m })
            .ToDictionaryAsync(x => x.CustomerId, x => x.Total, ct);

        // Cancelled invoices are neither an obligation nor a claim on the pool — any payment left
        // linked to one is released back to the pool, because its PaidAmount is not counted below.
        var invoices = await db.Invoices
            .Where(i => customerIds.Contains(i.CustomerId) && i.Status != InvoiceStatus.Cancelled)
            .Select(i => new { i.Id, i.CustomerId, i.InvoiceDate, i.CreatedAt, i.TotalAmount, i.PaidAmount })
            .ToListAsync(ct);

        // An order billed by an invoice is settled at the invoice level, so it is not its own obligation.
        var orders = await db.Orders
            .Where(o => customerIds.Contains(o.CustomerId)
                && o.Status == OrderStatus.Delivered
                && !db.Invoices.Any(inv => inv.CustomerId == o.CustomerId
                    && inv.Status != InvoiceStatus.Cancelled
                    && inv.PeriodFrom != null && inv.PeriodTo != null
                    && o.OrderDate >= inv.PeriodFrom && o.OrderDate <= inv.PeriodTo))
            .Select(o => new
            {
                o.Id,
                o.CustomerId,
                o.OrderDate,
                o.CreatedAt,
                Total = o.OrderItems.Sum(i => (decimal?)(i.Quantity * i.UnitRate)) ?? 0m
            })
            .ToListAsync(ct);

        var invoicesByCustomer = invoices.ToLookup(i => i.CustomerId);
        var ordersByCustomer = orders.ToLookup(o => o.CustomerId);
        var result = Result.Empty();

        foreach (var customerId in customerIds.Distinct())
        {
            var mine = invoicesByCustomer[customerId].ToList();

            // Money already sitting on invoices is spoken for; whatever else they've paid is free.
            var committed = mine.Sum(i => i.PaidAmount);
            var pool = Math.Max(0m, paid.GetValueOrDefault(customerId, 0m) - committed);

            var ladder = mine
                .Select(i => new Obligation(
                    i.Id, true, i.InvoiceDate, i.CreatedAt, Math.Max(0m, i.TotalAmount - i.PaidAmount)))
                .Concat(ordersByCustomer[customerId]
                    .Select(o => new Obligation(o.Id, false, o.OrderDate, o.CreatedAt, Math.Max(0m, o.Total))))
                .OrderBy(o => o.Date).ThenBy(o => o.CreatedAt);

            foreach (var o in ladder)
            {
                var applied = Math.Min(o.Outstanding, pool);
                if (o.IsInvoice) result.Invoices[o.Id] = applied;
                else result.Orders[o.Id] = applied;
                pool -= applied;
            }
        }

        return result;
    }
}
