using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Orders;

/// <summary>
/// Spreads a customer's unallocated payments across their delivered, not-yet-invoiced orders
/// oldest-first (FIFO), so a single lump-sum or advance payment correctly settles older orders
/// even though it isn't linked to each one (guide §9). Invoiced orders are excluded — their money
/// is reconciled at the invoice level.
/// </summary>
public static class OrderPaymentAllocator
{
    /// <summary>FIFO spread of <paramref name="pool"/> over orders given oldest-first; returns order id → amount applied.</summary>
    public static Dictionary<Guid, decimal> Allocate(
        decimal pool, IEnumerable<(Guid Id, decimal Total)> ordersOldestFirst)
    {
        var result = new Dictionary<Guid, decimal>();
        var remaining = pool;
        foreach (var (id, total) in ordersOldestFirst)
        {
            var applied = Math.Min(total, Math.Max(0m, remaining));
            result[id] = applied;
            remaining -= applied;
        }
        return result;
    }

    /// <summary>
    /// Computes the FIFO-applied amount for every delivered, uninvoiced order of the given customers.
    /// Pool = completed payments NOT applied to an invoice (doorstep collections + standalone advances).
    /// </summary>
    public static async Task<Dictionary<Guid, decimal>> ComputeAsync(
        IAppDbContext db, IReadOnlyCollection<Guid> customerIds, CancellationToken ct)
    {
        var result = new Dictionary<Guid, decimal>();
        if (customerIds.Count == 0) return result;

        var pools = await db.Payments
            .Where(p => customerIds.Contains(p.CustomerId)
                && p.Status == PaymentStatus.Completed && p.InvoiceId == null)
            .GroupBy(p => p.CustomerId)
            .Select(g => new { CustomerId = g.Key, Pool = g.Sum(x => (decimal?)x.Amount) ?? 0m })
            .ToDictionaryAsync(x => x.CustomerId, x => x.Pool, ct);

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

        foreach (var grp in orders.GroupBy(o => o.CustomerId))
        {
            var pool = pools.GetValueOrDefault(grp.Key, 0m);
            var ordered = grp.OrderBy(o => o.OrderDate).ThenBy(o => o.CreatedAt).Select(o => (o.Id, o.Total));
            foreach (var (id, applied) in Allocate(pool, ordered))
                result[id] = applied;
        }

        return result;
    }
}
