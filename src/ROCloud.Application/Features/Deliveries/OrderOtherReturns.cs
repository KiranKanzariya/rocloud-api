using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Deliveries;

/// <summary>
/// "Other empties" for a set of orders: jars the customer returned during a delivery for a product
/// that was NOT on the order (e.g. a 20L brought back on an 18L run). They are stored as order-scoped
/// Return inventory movements, so they don't show up in the order's own line items — this reconstructs
/// them per order so the board, the orders list and the order detail can all surface them.
///
/// Grouped in memory after plain Where/Select queries (no GroupBy inside a projection), so it
/// translates on Npgsql exactly as it runs on the in-memory test provider.
/// </summary>
public static class OrderOtherReturns
{
    public sealed record Line(string ProductName, string BottleSize, int Quantity);

    public static async Task<Dictionary<Guid, List<Line>>> ComputeAsync(
        IAppDbContext db, IReadOnlyCollection<Guid> orderIds, CancellationToken ct)
    {
        var result = new Dictionary<Guid, List<Line>>();
        if (orderIds.Count == 0) return result;

        var moves = await db.InventoryMovements
            .Where(m => m.OrderId != null && orderIds.Contains(m.OrderId.Value)
                        && m.MovementType == InventoryMovementType.Return)
            .Select(m => new { OrderId = m.OrderId!.Value, m.ProductId, m.Quantity })
            .ToListAsync(ct);
        if (moves.Count == 0) return result;

        // Each order's own products, so we can drop returns that ARE on the order (those are the
        // normal per-item returns, shown elsewhere).
        var onOrder = (await db.OrderItems
                .Where(oi => orderIds.Contains(oi.OrderId))
                .Select(oi => new { oi.OrderId, oi.ProductId })
                .ToListAsync(ct))
            .GroupBy(x => x.OrderId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ProductId).ToHashSet());

        var other = moves
            .Where(m => !(onOrder.TryGetValue(m.OrderId, out var set) && set.Contains(m.ProductId)))
            .ToList();
        if (other.Count == 0) return result;

        var productIds = other.Select(m => m.ProductId).Distinct().ToList();
        var products = (await db.Products
                .Where(p => productIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Name, p.BottleSize })
                .ToListAsync(ct))
            .ToDictionary(p => p.Id, p => (p.Name, Size: p.BottleSize.ToWire()));

        foreach (var byOrder in other.GroupBy(m => m.OrderId))
        {
            result[byOrder.Key] = byOrder
                .GroupBy(m => m.ProductId)
                .Select(pg =>
                {
                    products.TryGetValue(pg.Key, out var p);
                    return new Line(p.Name ?? string.Empty, p.Size ?? string.Empty, pg.Sum(x => x.Quantity));
                })
                .OrderBy(l => l.ProductName)
                .ToList();
        }

        return result;
    }
}
