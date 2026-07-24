using ROCloud.Application.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Orders.Dtos;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Orders.Queries.GetProductionPlan;

/// <summary>
/// The production plan: for each upcoming day, how many units of each product the plant must prepare,
/// aggregated across every open future booking. This is the point of advance notice — the owner sees
/// "on the 20th I need 200 jars" and can stock/produce ahead. It's a PLANNING number: inventory only
/// moves when jars physically change hands at delivery, so nothing is reserved here.
/// From/To default to [today+1, today+30]; only open orders (Pending/Confirmed) count.
/// </summary>
public sealed record GetProductionPlanQuery(DateOnly? From = null, DateOnly? To = null)
    : IRequest<IReadOnlyList<ProductionPlanDayDto>>;

public class GetProductionPlanQueryHandler
    : IRequestHandler<GetProductionPlanQuery, IReadOnlyList<ProductionPlanDayDto>>
{
    private readonly IAppDbContext _db;

    public GetProductionPlanQueryHandler(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<ProductionPlanDayDto>> Handle(
        GetProductionPlanQuery request, CancellationToken ct)
    {
        var today = AppTimeZone.Today(DateTime.UtcNow);
        var from = request.From ?? today.AddDays(1);
        var to = request.To ?? today.AddDays(30);
        if (to < from) (from, to) = (to, from);

        var open = new[] { OrderStatus.Pending, OrderStatus.Confirmed };

        // Pull the item-level rows for open orders in the window, then aggregate in memory (keeps the
        // shape identical on Postgres and the InMemory test provider — no GroupBy translation quirks).
        var rows = await _db.Orders
            .Where(o => o.OrderDate >= from && o.OrderDate <= to && open.Contains(o.Status))
            .SelectMany(o => o.OrderItems.Select(i => new
            {
                o.Id,
                o.OrderDate,
                o.OrderType,
                CustomerName = o.Customer != null ? o.Customer.Name : string.Empty,
                AreaName = o.Area != null ? o.Area.Name : null,
                i.ProductId,
                ProductName = i.Product != null ? i.Product.Name : string.Empty,
                i.Quantity
            }))
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.OrderDate)
            .OrderBy(g => g.Key)
            .Select(day => new ProductionPlanDayDto(
                Date: day.Key,
                TotalUnits: day.Sum(r => r.Quantity),
                OrderCount: day.Select(r => r.Id).Distinct().Count(),
                Lines: day
                    .GroupBy(r => new { r.ProductId, r.ProductName })
                    .OrderByDescending(pg => pg.Sum(r => r.Quantity))
                    .Select(pg => new ProductionPlanLineDto(
                        pg.Key.ProductId, pg.Key.ProductName,
                        pg.Sum(r => r.Quantity),
                        pg.Select(r => r.Id).Distinct().Count()))
                    .ToList(),
                Bookings: day
                    .GroupBy(r => new { r.Id, r.CustomerName, r.AreaName, r.OrderType })
                    .OrderByDescending(bg => bg.Sum(r => r.Quantity))
                    .Select(bg => new ProductionPlanBookingDto(
                        bg.Key.Id, bg.Key.CustomerName, bg.Key.AreaName,
                        bg.Key.OrderType.ToString(), bg.Sum(r => r.Quantity),
                        bg.OrderByDescending(r => r.Quantity)
                            .Select(r => new OrderLineSummaryDto(r.ProductName, r.Quantity))
                            .ToList()))
                    .ToList()))
            .ToList();
    }
}
