using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Deliveries.Dtos;
using ROCloud.Application.Features.Deliveries.Queries.GetDeliveries;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Deliveries.Queries.GetDeliveryProductTotals;

/// <summary>
/// Per-product jar totals across the deliveries matching the filter (e.g. date = today) — so the
/// dashboard's "Today's deliveries" card can show the load broken down by product, not just a count.
/// Sums the ordered quantity of each product across those stops. Cancelled orders are excluded.
/// </summary>
public sealed record GetDeliveryProductTotalsQuery(DeliveryFilterDto Filter)
    : IRequest<IReadOnlyList<BoardItemTotalDto>>;

public class GetDeliveryProductTotalsQueryHandler
    : IRequestHandler<GetDeliveryProductTotalsQuery, IReadOnlyList<BoardItemTotalDto>>
{
    private readonly IAppDbContext _db;

    public GetDeliveryProductTotalsQueryHandler(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<BoardItemTotalDto>> Handle(
        GetDeliveryProductTotalsQuery request, CancellationToken ct)
    {
        var orderIds = await GetDeliveriesQueryHandler.ApplyFilter(_db.Deliveries, request.Filter)
            .Where(d => d.Order == null || d.Order.Status != OrderStatus.Cancelled)
            .Select(d => d.OrderId)
            .Distinct()
            .ToListAsync(ct);
        if (orderIds.Count == 0) return [];

        var totals = await _db.OrderItems
            .Where(i => orderIds.Contains(i.OrderId))
            .GroupBy(i => i.ProductId)
            .Select(g => new { ProductId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToListAsync(ct);
        if (totals.Count == 0) return [];

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
