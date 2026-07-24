using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Deliveries;
using ROCloud.Application.Features.Orders.Dtos;
using ROCloud.Application.Features.Payments;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Orders.Queries.GetOrderById;

public sealed record GetOrderByIdQuery(Guid Id) : IRequest<OrderDto>;

public class GetOrderByIdQueryHandler : IRequestHandler<GetOrderByIdQuery, OrderDto>
{
    private readonly IAppDbContext _db;

    public GetOrderByIdQueryHandler(IAppDbContext db) => _db = db;

    public async Task<OrderDto> Handle(GetOrderByIdQuery request, CancellationToken ct)
    {
        // OrderItems and Delivery.Items are two collection navigations, so a single JOIN would return
        // |OrderItems| × |Delivery.Items| rows with the Order + Customer + Area payload duplicated
        // across every one. The Npgsql registration defaults to split queries for exactly this reason
        // (see Infrastructure/DependencyInjection) — there is no per-query AsSplitQuery here because
        // that lives in EF Core *Relational*, which this layer deliberately does not reference.
        var order = await _db.Orders
            .Include(o => o.Customer)
            .Include(o => o.Area)
            .Include(o => o.OrderItems).ThenInclude(i => i.Product)
            .Include(o => o.Delivery).ThenInclude(d => d!.Items)
            .FirstOrDefaultAsync(o => o.Id == request.Id, ct)
            ?? throw new NotFoundException("Order", request.Id);

        var deliveryBoyName = order.DeliveryBoyId is { } boyId
            ? await _db.Users.Where(u => u.Id == boyId).Select(u => u.Name).FirstOrDefaultAsync(ct)
            : null;

        var total = order.OrderItems.Sum(i => i.Quantity * i.UnitRate);
        var invoiced = await _db.Invoices.AnyAsync(inv => inv.CustomerId == order.CustomerId
            && inv.Status != InvoiceStatus.Cancelled
            && inv.PeriodFrom != null && inv.PeriodTo != null
            && order.OrderDate >= inv.PeriodFrom && order.OrderDate <= inv.PeriodTo, ct);

        // FIFO-applied amount for this order (only relevant when delivered + uninvoiced). The pool is
        // drained by the customer's older obligations — open invoices included — before it reaches here.
        var allocations = order.Status == OrderStatus.Delivered && !invoiced
            ? (await CustomerObligationAllocator.ComputeAsync(_db, new[] { order.CustomerId }, ct)).Orders
            : new Dictionary<Guid, decimal>();
        var (amountPaid, paymentStatus) = OrderPaymentStatus.Resolve(
            order.Status, invoiced, total, allocations.GetValueOrDefault(order.Id, 0m));

        var items = order.OrderItems
            .Select(i => new OrderItemDto(
                i.Id, i.ProductId,
                i.Product?.Name ?? string.Empty,
                i.Quantity, i.UnitRate, i.Quantity * i.UnitRate))
            .ToList();

        // Per-product out/back for the delivery. A delivery item can reference a product NOT on the
        // order (an empty of another size brought back), so resolve names from a lookup that also
        // covers those — the order's own OrderItems don't include them.
        OrderDeliveryDto? delivery = null;
        if (order.Delivery is { } d)
        {
            var deliveryProductIds = d.Items.Select(di => di.ProductId).Distinct().ToList();
            var productNames = deliveryProductIds.Count == 0
                ? new Dictionary<Guid, (string Name, string Size)>()
                : await _db.Products
                    .Where(p => deliveryProductIds.Contains(p.Id))
                    .Select(p => new { p.Id, p.Name, p.BottleSize })
                    .ToDictionaryAsync(p => p.Id, p => (Name: p.Name, Size: p.BottleSize.ToWire()), ct);

            var deliveryItems = d.Items
                .Select(di =>
                {
                    productNames.TryGetValue(di.ProductId, out var p);
                    return new OrderDeliveryItemDto(
                        p.Name ?? string.Empty, p.Size ?? string.Empty, di.JarsDelivered, di.JarsReturned);
                })
                .OrderBy(x => x.ProductName)
                .ToList();

            // Empties returned for a product not on this order (order-scoped Return movements).
            var otherByOrder = await OrderOtherReturns.ComputeAsync(_db, new[] { order.Id }, ct);
            var otherReturns = (otherByOrder.GetValueOrDefault(order.Id) ?? [])
                .Select(l => new OrderOtherReturnDto(l.ProductName, l.BottleSize, l.Quantity))
                .ToList();

            delivery = new OrderDeliveryDto(
                d.Id, d.Status.ToString(), d.ScheduledDate, d.DeliveredAt,
                d.JarsDelivered, d.JarsReturned, d.CollectedAmount,
                d.PaymentMethod?.ToString(), d.ProofImageUrl, deliveryItems, otherReturns);
        }

        return new OrderDto(
            order.Id, order.OrderDate, order.CustomerId,
            order.Customer?.Name ?? string.Empty,
            order.Customer?.Mobile,
            order.AreaId,
            order.Area?.Name,
            order.DeliveryBoyId, deliveryBoyName,
            order.OrderType.ToString(), order.DeliveryMode.ToString(), order.Status.ToString(),
            order.Notes,
            total,
            amountPaid,
            paymentStatus,
            order.CreatedAt,
            items, delivery);
    }
}
