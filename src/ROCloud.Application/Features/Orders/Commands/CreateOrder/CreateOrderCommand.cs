using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Orders.Dtos;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Features.Orders.Commands.CreateOrder;

public sealed record CreateOrderCommand(
    Guid CustomerId,
    DateOnly? OrderDate,
    string? OrderType,
    string? Notes,
    IReadOnlyList<CreateOrderItemDto> Items,
    // For a "Both" customer, which fulfilment this order uses (HomeDelivery/PlantPickup). Ignored for
    // single-mode customers (their mode is fixed); defaults to HomeDelivery when omitted.
    string? DeliveryMode = null) : IRequest<Guid>;

public class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(c => c.CustomerId).NotEmpty();
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

public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, Guid>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<CreateOrderCommandHandler> _logger;

    public CreateOrderCommandHandler(
        IAppDbContext db, ITenantContext tenant, ICurrentUserService currentUser,
        ILogger<CreateOrderCommandHandler> logger)
    {
        _db = db;
        _tenant = tenant;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<Guid> Handle(CreateOrderCommand request, CancellationToken ct)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct)
                       ?? throw new NotFoundException("Customer", request.CustomerId);

        // Validate every product exists in the tenant and capture its default rate.
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

        var orderDate = request.OrderDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

        // An order is always concretely HomeDelivery or PlantPickup. Single-mode customers are fixed;
        // a "Both" customer chooses per order (the request's choice, defaulting to HomeDelivery).
        var orderMode = customer.DeliveryMode switch
        {
            DeliveryMode.HomeDelivery => DeliveryMode.HomeDelivery,
            DeliveryMode.PlantPickup => DeliveryMode.PlantPickup,
            _ => request.DeliveryMode is { } m ? Enum.Parse<DeliveryMode>(m) : DeliveryMode.HomeDelivery
        };

        // Plant-pickup orders collect from the plant — no delivery boy / route assignment.
        var deliveryBoyId = orderMode == DeliveryMode.PlantPickup
            ? null
            : await DeliveryBoyResolver.ResolveAsync(_db, customer.AreaId, ct);

        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            CustomerId = customer.Id,
            AreaId = customer.AreaId,
            DeliveryBoyId = deliveryBoyId,
            OrderDate = orderDate,
            OrderType = request.OrderType is { } ot ? Enum.Parse<OrderType>(ot) : OrderType.Regular,
            DeliveryMode = orderMode,
            Status = OrderStatus.Pending,
            Notes = request.Notes,
            CreatedBy = _currentUser.UserId
        };

        foreach (var item in request.Items)
        {
            var rate = item.UnitRate ?? products[item.ProductId].DefaultRate;
            order.OrderItems.Add(new OrderItem
            {
                Id = Guid.NewGuid(),
                TenantId = _tenant.TenantId,
                OrderId = order.Id,
                ProductId = item.ProductId,
                Quantity = item.Quantity,
                UnitRate = rate
                // TotalAmount is a stored generated column (quantity * unit_rate) — never set here.
            });
        }

        // 1:1 delivery, auto-created in Pending state for the delivery board.
        order.Delivery = new Delivery
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            OrderId = order.Id,
            DeliveryBoyId = deliveryBoyId,
            ScheduledDate = orderDate,
            Status = DeliveryStatus.Pending
        };

        _db.Orders.Add(order);

        // One SaveChanges → one DB transaction (atomic for order + items + delivery).
        await _db.SaveChangesAsync(ct);

        // Inventory is a bottle float: stock moves when jars physically change hands at
        // delivery (issued/returned), not at order creation — see UpdateDeliveryStatus,
        // which calls IInventoryService. Nothing to decrement here.
        _logger.LogInformation(
            "Order {OrderId} created with {ItemCount} item(s); inventory updates on delivery.",
            order.Id, order.OrderItems.Count);

        return order.Id;
    }
}
