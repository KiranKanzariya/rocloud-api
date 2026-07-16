using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Deliveries.Dtos;
using ROCloud.Application.Features.Deliveries.Queries.GetDeliveries;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Deliveries.Queries.GetDeliveryBoard;

/// <summary>Returns deliveries grouped by status for the kanban delivery board.</summary>
public sealed record GetDeliveryBoardQuery(DeliveryFilterDto Filter) : IRequest<DeliveryBoardDto>;

public class GetDeliveryBoardQueryHandler : IRequestHandler<GetDeliveryBoardQuery, DeliveryBoardDto>
{
    private readonly IAppDbContext _db;

    public GetDeliveryBoardQueryHandler(IAppDbContext db) => _db = db;

    public async Task<DeliveryBoardDto> Handle(GetDeliveryBoardQuery request, CancellationToken ct)
    {
        // The board is a single-day operational view. If the caller supplies no date window at all the
        // query would materialise every delivery ever — so default to today. The owner portal always
        // sends a date, so this only bounds stray/unfiltered callers (no behaviour change for the portal).
        var filter = request.Filter;
        if (filter is { Date: null, FromDate: null, ToDate: null })
            filter = filter with { Date = AppTimeZone.Today(DateTime.UtcNow) };

        // Cancelling an order leaves its delivery row behind as Skipped, so the board must exclude
        // them explicitly — otherwise they resurface as "Delivered" stops or "Awaiting pickup" cards.
        var query = GetDeliveriesQueryHandler.ApplyFilter(_db.Deliveries, filter)
            .Where(d => d.Order == null || d.Order.Status != OrderStatus.Cancelled);

        var all = await query
            .OrderBy(d => d.ScheduledDate).ThenBy(d => d.Id)
            .ToListItem(_db)
            .ToListAsync(ct);

        // Off-order empties (product not on the order) for the delivered stops, filled in after the
        // SQL projection since it needs an in-memory group.
        var otherReturns = await OrderOtherReturns.ComputeAsync(
            _db, all.Where(d => d.Status == nameof(DeliveryStatus.Delivered)).Select(d => d.OrderId).ToList(), ct);
        foreach (var d in all)
            if (otherReturns.TryGetValue(d.OrderId, out var lines))
                d.OtherReturns = lines.Select(l => new DeliveredOtherReturnDto(l.ProductName, l.Quantity)).ToList();

        // Plant-pickup stops have no route/van leg — pull them into their own section so the status
        // columns (and the van-load total below) reflect home deliveries only. Awaiting-pickup stops
        // come first (they're the ones still needing action); completed/failed sink below. Within a
        // group the scheduled-date order from above is preserved.
        var pickups = all
            .Where(d => d.DeliveryMode == nameof(DeliveryMode.PlantPickup))
            .OrderBy(d => PickupOrder(d.Status))
            .ToList();
        var route = all.Where(d => d.DeliveryMode != nameof(DeliveryMode.PlantPickup)).ToList();

        var pending = route.Where(d => d.Status == nameof(DeliveryStatus.Pending)).ToList();
        var inTransit = route.Where(d => d.Status == nameof(DeliveryStatus.InTransit)).ToList();
        // "Delivered" column shows successfully completed stops (skipped is treated as completed too).
        var delivered = route.Where(d =>
            d.Status == nameof(DeliveryStatus.Delivered)
            || d.Status == nameof(DeliveryStatus.Skipped)).ToList();
        // Failed stops get their own column so they aren't buried under "Delivered".
        var failed = route.Where(d => d.Status == nameof(DeliveryStatus.Failed)).ToList();

        // Item-wise jar totals still to be delivered (Pending + In transit) — the load to put on the
        // van. Pickups are excluded (they're collected at the plant, not loaded).
        var toDeliver = await BuildToDeliverAsync(pending.Concat(inTransit).Select(d => d.OrderId), ct);

        return new DeliveryBoardDto(pending, inTransit, delivered, failed, pickups, toDeliver);
    }

    /// <summary>Sort key for the pickups section: awaiting-pickup first, then done, then failed.</summary>
    private static int PickupOrder(string status) => status switch
    {
        nameof(DeliveryStatus.Pending) => 0,
        nameof(DeliveryStatus.InTransit) => 0,   // an in-progress pickup is still "awaiting", show up top
        nameof(DeliveryStatus.Delivered) => 1,
        nameof(DeliveryStatus.Skipped) => 1,
        _ => 2                                    // Failed (and anything else) last
    };

    /// <summary>Sums order-item quantities by product across the given (outstanding) orders.</summary>
    private async Task<IReadOnlyList<BoardItemTotalDto>> BuildToDeliverAsync(
        IEnumerable<Guid> orderIds, CancellationToken ct)
    {
        var ids = orderIds.Distinct().ToList();
        if (ids.Count == 0) return [];

        var totals = await _db.OrderItems
            .Where(i => ids.Contains(i.OrderId))
            .GroupBy(i => i.ProductId)
            .Select(g => new { ProductId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToListAsync(ct);

        var productIds = totals.Select(t => t.ProductId).ToList();
        var products = await _db.Products
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.BottleSize })
            .ToListAsync(ct);

        return totals
            .Select(t =>
            {
                var p = products.FirstOrDefault(x => x.Id == t.ProductId);
                return new BoardItemTotalDto(p?.Name ?? string.Empty, p?.BottleSize.ToWire() ?? string.Empty, t.Quantity);
            })
            .OrderByDescending(t => t.Quantity)
            .ThenBy(t => t.ProductName)
            .ToList();
    }
}
