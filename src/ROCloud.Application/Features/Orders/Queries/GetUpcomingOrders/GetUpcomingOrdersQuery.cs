using ROCloud.Application.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Orders.Dtos;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Orders.Queries.GetUpcomingOrders;

/// <summary>
/// Future-dated bookings the owner has committed to but that haven't reached their delivery day yet
/// (OrderDate strictly after today, still open). These sit off the day-scoped delivery board until
/// their date arrives, so this is the only place the owner sees what's coming — typically Advance
/// (event/program) orders, but any manually future-dated order shows here too.
/// </summary>
public sealed record GetUpcomingOrdersQuery(int Days = 60) : IRequest<IReadOnlyList<UpcomingOrderDto>>;

public class GetUpcomingOrdersQueryHandler
    : IRequestHandler<GetUpcomingOrdersQuery, IReadOnlyList<UpcomingOrderDto>>
{
    private readonly IAppDbContext _db;

    public GetUpcomingOrdersQueryHandler(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<UpcomingOrderDto>> Handle(
        GetUpcomingOrdersQuery request, CancellationToken ct)
    {
        var today = AppTimeZone.Today(DateTime.UtcNow);
        var horizon = today.AddDays(Math.Clamp(request.Days, 1, 365));

        // Only still-open bookings — a cancelled or already-delivered order isn't "upcoming".
        var open = new[] { OrderStatus.Pending, OrderStatus.Confirmed };

        var rows = await _db.Orders
            .Where(o => o.OrderDate > today && o.OrderDate <= horizon && open.Contains(o.Status))
            .OrderBy(o => o.OrderDate).ThenBy(o => o.CreatedAt)
            .Select(o => new
            {
                o.Id,
                o.OrderDate,
                o.CreatedAt,
                CustomerName = o.Customer != null ? o.Customer.Name : string.Empty,
                CustomerMobile = o.Customer != null ? o.Customer.Mobile : null,
                AreaName = o.Area != null ? o.Area.Name : null,
                DeliveryBoyName = _db.Users
                    .Where(u => u.Id == o.DeliveryBoyId).Select(u => u.Name).FirstOrDefault(),
                o.OrderType,
                o.DeliveryMode,
                o.Status,
                TotalAmount = o.OrderItems.Sum(i => (decimal?)(i.Quantity * i.UnitRate)) ?? 0m,
                // The line items, so the owner sees what's actually booked (largest quantity first).
                Items = o.OrderItems
                    .OrderByDescending(i => i.Quantity)
                    .Select(i => new OrderLineSummaryDto(
                        i.Product != null ? i.Product.Name : string.Empty, i.Quantity))
                    .ToList()
            })
            .ToListAsync(ct);

        return rows.Select(r => new UpcomingOrderDto(
            r.Id, r.OrderDate, r.CustomerName, r.CustomerMobile, r.AreaName, r.DeliveryBoyName,
            r.OrderType.ToString(), r.DeliveryMode.ToString(), r.Status.ToString(),
            r.Items.Count, r.Items.Sum(i => i.Quantity), r.TotalAmount, r.Items, r.CreatedAt)).ToList();
    }
}
