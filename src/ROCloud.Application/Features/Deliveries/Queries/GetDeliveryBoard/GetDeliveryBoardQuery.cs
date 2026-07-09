using MediatR;
using Microsoft.EntityFrameworkCore;
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
        var query = GetDeliveriesQueryHandler.ApplyFilter(_db.Deliveries, request.Filter);

        var all = await query
            .OrderBy(d => d.ScheduledDate).ThenBy(d => d.Id)
            .ToListItem(_db)
            .ToListAsync(ct);

        // Plant-pickup stops have no route/van leg — pull them into their own section so the status
        // columns (and the van-load total below) reflect home deliveries only.
        var pickups = all.Where(d => d.DeliveryMode == nameof(DeliveryMode.PlantPickup)).ToList();
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
