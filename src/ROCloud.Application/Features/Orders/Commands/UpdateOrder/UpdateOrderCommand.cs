using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Orders.Dtos;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Features.Orders.Commands.UpdateOrder;

/// <summary>
/// Edits an order's items / notes / date while it is still editable — i.e. before it leaves for
/// delivery (Pending or Confirmed). Once InTransit/Delivered/Cancelled it is locked. The customer
/// is never changed here. Inventory isn't touched (jars only move at delivery).
/// </summary>
public sealed record UpdateOrderCommand(
    Guid Id,
    DateOnly? OrderDate,
    string? OrderType,
    string? Notes,
    IReadOnlyList<CreateOrderItemDto> Items,
    // For a "Both" customer: switch this order's fulfilment (HomeDelivery/PlantPickup). Re-resolves
    // the delivery boy when it changes. Ignored for single-mode customers.
    string? DeliveryMode = null) : IRequest;

public class UpdateOrderCommandValidator : AbstractValidator<UpdateOrderCommand>
{
    public UpdateOrderCommandValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Items).NotEmpty().WithMessage("An order must contain at least one item.");
        RuleForEach(c => c.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).NotEmpty();
            item.RuleFor(i => i.Quantity).GreaterThan(0);
            item.RuleFor(i => i.UnitRate).GreaterThanOrEqualTo(0).When(i => i.UnitRate.HasValue);
        });
        RuleFor(c => c.OrderType)
            .Must(v => v is null || Enum.GetNames<OrderType>().Contains(v))
            .WithMessage("Invalid order type.");
        RuleFor(c => c.DeliveryMode)
            .Must(v => v is null
                || v == nameof(Domain.Enums.DeliveryMode.HomeDelivery)
                || v == nameof(Domain.Enums.DeliveryMode.PlantPickup))
            .WithMessage("Order delivery mode must be HomeDelivery or PlantPickup.");
    }
}

public class UpdateOrderCommandHandler : IRequestHandler<UpdateOrderCommand>
{
    private static readonly OrderStatus[] Editable = [OrderStatus.Pending, OrderStatus.Confirmed];

    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;

    public UpdateOrderCommandHandler(IAppDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task Handle(UpdateOrderCommand request, CancellationToken ct)
    {
        var order = await _db.Orders
            .Include(o => o.OrderItems)
            .Include(o => o.Delivery)
            .FirstOrDefaultAsync(o => o.Id == request.Id, ct)
            ?? throw new NotFoundException("Order", request.Id);

        if (!Editable.Contains(order.Status))
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["status"] = ["This order can no longer be edited — it has already been dispatched or closed."]
            });

        var productIds = request.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _db.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        var missing = productIds.Where(id => !products.ContainsKey(id)).ToList();
        if (missing.Count > 0)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["items"] = [$"Unknown product(s): {string.Join(", ", missing)}"]
            });

        // Reconcile the line items IN PLACE — update the quantity/rate of products already on the order,
        // insert genuinely new ones, and delete dropped ones. This is deliberately NOT a delete-all +
        // re-insert: re-using existing rows keeps the edit idempotent, so a duplicate/retried PUT (e.g. a
        // double-submit or a proxy retry) just re-applies the same values instead of failing with a
        // concurrency error because the first request already deleted the rows.
        //
        // Freeze-on-edit falls out naturally: a product already on the order keeps its original UnitRate
        // (the existing row's value) unless an explicit rate is supplied; a new product line takes the
        // current catalogue rate.
        var existingItems = order.OrderItems.ToList();
        var matched = new HashSet<Guid>();
        foreach (var item in request.Items)
        {
            var line = existingItems.FirstOrDefault(e => e.ProductId == item.ProductId && !matched.Contains(e.Id));
            if (line is not null)
            {
                matched.Add(line.Id);
                line.Quantity = item.Quantity;
                if (item.UnitRate is { } explicitRate)
                    line.UnitRate = explicitRate;   // else keep the original (frozen) rate
            }
            else
            {
                // Add via the DbSet (not the navigation collection) so EF marks the row Added → INSERT.
                // Adding a pre-keyed entity to the tracked navigation collection makes EF treat the
                // store-generated Guid key as an existing row → Modified → an UPDATE that affects 0 rows.
                _db.OrderItems.Add(new OrderItem
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenant.TenantId,
                    OrderId = order.Id,
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    UnitRate = item.UnitRate ?? products[item.ProductId].DefaultRate
                });
            }
        }
        foreach (var dropped in existingItems.Where(e => !matched.Contains(e.Id)))
            _db.OrderItems.Remove(dropped);

        if (request.OrderType is { } ot)
            order.OrderType = Enum.Parse<OrderType>(ot);
        order.Notes = request.Notes;
        if (request.OrderDate is { } date)
        {
            order.OrderDate = date;
            if (order.Delivery is { } delivery)
                delivery.ScheduledDate = date; // keep the 1:1 delivery's schedule in sync
        }

        await ApplyDeliveryModeAsync(order, request, ct);

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Resolves the order's concrete fulfilment. Single-mode customers are fixed; a "Both" customer can
    /// switch per order. When the mode changes, the delivery boy is re-resolved (cleared for pickup).
    /// </summary>
    private async Task ApplyDeliveryModeAsync(Order order, UpdateOrderCommand request, CancellationToken ct)
    {
        var customerMode = await _db.Customers
            .Where(c => c.Id == order.CustomerId)
            .Select(c => c.DeliveryMode)
            .FirstOrDefaultAsync(ct);

        var newMode = customerMode switch
        {
            DeliveryMode.HomeDelivery => DeliveryMode.HomeDelivery,
            DeliveryMode.PlantPickup => DeliveryMode.PlantPickup,
            _ => request.DeliveryMode is { } m ? Enum.Parse<DeliveryMode>(m) : order.DeliveryMode // Both
        };

        if (newMode == order.DeliveryMode)
            return;

        order.DeliveryMode = newMode;
        var boyId = newMode == DeliveryMode.PlantPickup
            ? null
            : await DeliveryBoyResolver.ResolveAsync(_db, order.AreaId, ct);
        order.DeliveryBoyId = boyId;
        if (order.Delivery is { } delivery)
            delivery.DeliveryBoyId = boyId;
    }
}
