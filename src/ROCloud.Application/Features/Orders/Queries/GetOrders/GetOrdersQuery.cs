using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Models;
using ROCloud.Application.Features.Deliveries;
using ROCloud.Application.Features.Orders.Dtos;
using ROCloud.Application.Features.Payments;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Orders.Queries.GetOrders;

public sealed record GetOrdersQuery(OrderFilterDto Filter) : IRequest<PagedResult<OrderListItemDto>>;

public class GetOrdersQueryHandler : IRequestHandler<GetOrdersQuery, PagedResult<OrderListItemDto>>
{
    private readonly IAppDbContext _db;

    public GetOrdersQueryHandler(IAppDbContext db) => _db = db;

    public async Task<PagedResult<OrderListItemDto>> Handle(GetOrdersQuery request, CancellationToken ct)
    {
        var f = request.Filter;
        var page = Math.Max(1, f.Page);
        var pageSize = Math.Clamp(f.PageSize, 1, 100);

        IQueryable<Order> query = _db.Orders;

        if (f.FromDate is { } from) query = query.Where(o => o.OrderDate >= from);
        if (f.ToDate is { } to) query = query.Where(o => o.OrderDate <= to);
        if (f.AreaId is { } areaId) query = query.Where(o => o.AreaId == areaId);
        if (f.CustomerId is { } customerId) query = query.Where(o => o.CustomerId == customerId);
        if (f.DeliveryBoyId is { } boyId) query = query.Where(o => o.DeliveryBoyId == boyId);
        if (f.Status is not null && Enum.GetNames<OrderStatus>().Contains(f.Status))
        {
            var status = Enum.Parse<OrderStatus>(f.Status);
            query = query.Where(o => o.Status == status);
        }

        var descending = !string.Equals(f.SortDir, "asc", StringComparison.OrdinalIgnoreCase);
        // Every sort needs a tiebreaker. OrderDate is date-ONLY, so all of a day's orders tie on it —
        // without a secondary key their order is arbitrary (looks unsorted next to the time shown in the
        // Date column) AND unstable across pages. CreatedAt (the real placed-at instant) breaks the date
        // tie chronologically; Id makes it fully deterministic for pagination.
        IOrderedQueryable<Order> ordered = (f.SortBy?.ToLowerInvariant()) switch
        {
            "status" => descending ? query.OrderByDescending(o => o.Status) : query.OrderBy(o => o.Status),
            "customer" => descending
                ? query.OrderByDescending(o => o.Customer!.Name)
                : query.OrderBy(o => o.Customer!.Name),
            _ => descending
                ? query.OrderByDescending(o => o.OrderDate).ThenByDescending(o => o.CreatedAt)
                : query.OrderBy(o => o.OrderDate).ThenBy(o => o.CreatedAt)
        };
        query = descending
            ? ordered.ThenByDescending(o => o.Id)
            : ordered.ThenBy(o => o.Id);

        var total = await query.CountAsync(ct);

        var rows = await query
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(o => new
            {
                o.Id,
                o.OrderDate,
                o.CreatedAt,
                CustomerName = o.Customer != null ? o.Customer.Name : string.Empty,
                CustomerMobile = o.Customer != null ? o.Customer.Mobile : null,
                AreaName = o.Area != null ? o.Area.Name : null,
                DeliveryBoyName = _db.Users
                    .Where(u => u.Id == o.DeliveryBoyId)
                    .Select(u => u.Name)
                    .FirstOrDefault(),
                o.OrderType,
                o.DeliveryMode,
                o.Status,
                ItemCount = o.OrderItems.Count,
                o.CustomerId,
                Lines = o.OrderItems
                    .Select(oi => new OrderLineSummaryDto(oi.Product != null ? oi.Product.Name : string.Empty, oi.Quantity))
                    .ToList(),
                // Per-product out/back from the linked delivery's item rows (empty until delivered).
                DeliveredLines = o.Delivery == null
                    ? new List<OrderDeliveredLineDto>()
                    : o.Delivery.Items
                        .Select(di => new OrderDeliveredLineDto(
                            _db.Products.Where(p => p.Id == di.ProductId).Select(p => p.Name).FirstOrDefault() ?? string.Empty,
                            di.JarsDelivered,
                            di.JarsReturned))
                        .ToList(),
                // Computed from quantity * unit_rate so it works on InMemory (generated column is DB-only).
                TotalAmount = o.OrderItems.Sum(i => (decimal?)(i.Quantity * i.UnitRate)) ?? 0m,
                DeliveryStatus = o.Delivery != null ? (DeliveryStatus?)o.Delivery.Status : null,
                // Invoiced = a non-cancelled invoice's period covers this order (orders carry no invoice FK).
                Invoiced = _db.Invoices.Any(inv => inv.CustomerId == o.CustomerId
                    && inv.Status != InvoiceStatus.Cancelled
                    && inv.PeriodFrom != null && inv.PeriodTo != null
                    && o.OrderDate >= inv.PeriodFrom && o.OrderDate <= inv.PeriodTo)
            })
            .ToListAsync(ct);

        // FIFO-allocate each customer's payment pool across everything they owe so the per-order badge
        // reflects lump-sum / advance payments. The ladder spans ALL their obligations — not just this
        // page, and not just orders — so an older open invoice is settled before any order here is.
        var customerIds = rows
            .Where(r => r.Status == OrderStatus.Delivered && !r.Invoiced)
            .Select(r => r.CustomerId).Distinct().ToList();
        var allocations = (await CustomerObligationAllocator.ComputeAsync(_db, customerIds, ct)).Orders;

        // Off-order empties for this page's orders, in one batch.
        var otherReturns = await OrderOtherReturns.ComputeAsync(_db, rows.Select(r => r.Id).ToList(), ct);

        var items = rows.Select(r =>
        {
            var (amountPaid, paymentStatus) = OrderPaymentStatus.Resolve(
                r.Status, r.Invoiced, r.TotalAmount, allocations.GetValueOrDefault(r.Id, 0m));
            var other = (otherReturns.GetValueOrDefault(r.Id) ?? [])
                .Select(l => new OrderDeliveredOtherReturnDto(l.ProductName, l.Quantity)).ToList();
            return new OrderListItemDto(
                r.Id, r.OrderDate, r.CustomerName, r.CustomerMobile, r.AreaName, r.DeliveryBoyName,
                r.OrderType.ToString(), r.DeliveryMode.ToString(), r.Status.ToString(),
                r.ItemCount, r.TotalAmount, r.DeliveryStatus?.ToString(), amountPaid, paymentStatus, r.CreatedAt,
                r.Lines, r.DeliveredLines, other);
        }).ToList();

        return new PagedResult<OrderListItemDto>(items, total, page, pageSize);
    }
}
