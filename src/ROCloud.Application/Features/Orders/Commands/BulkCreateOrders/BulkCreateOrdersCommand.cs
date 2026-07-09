using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Orders.Dtos;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Orders.Commands.BulkCreateOrders;

/// <summary>
/// Creates one order per active subscription that is due on <see cref="TargetDate"/>
/// (default: today). Used by the "generate today's deliveries" action.
/// </summary>
public sealed record BulkCreateOrdersCommand(DateOnly? TargetDate) : IRequest<BulkCreateResultDto>;

public class BulkCreateOrdersCommandHandler : IRequestHandler<BulkCreateOrdersCommand, BulkCreateResultDto>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<BulkCreateOrdersCommandHandler> _logger;

    public BulkCreateOrdersCommandHandler(
        IAppDbContext db, ITenantContext tenant, ICurrentUserService currentUser,
        ILogger<BulkCreateOrdersCommandHandler> logger)
    {
        _db = db;
        _tenant = tenant;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<BulkCreateResultDto> Handle(BulkCreateOrdersCommand request, CancellationToken ct)
    {
        var targetDate = request.TargetDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var subscriptions = await _db.CustomerSubscriptions
            .Include(s => s.Customer)
            .Where(s => s.IsActive
                        && s.StartDate <= targetDate
                        && (s.EndDate == null || s.EndDate >= targetDate))
            .ToListAsync(ct);

        // Customers who already have an order on the target date — don't double-create.
        var alreadyOrdered = (await _db.Orders
                .Where(o => o.OrderDate == targetDate)
                .Select(o => o.CustomerId)
                .ToListAsync(ct))
            .ToHashSet();

        var considered = 0;
        var created = 0;
        var skipped = 0;

        foreach (var sub in subscriptions)
        {
            if (sub.Customer is null || sub.Customer.IsDeleted)
                continue;

            considered++;

            if (!IsDue(sub.Frequency, sub.StartDate, targetDate) || alreadyOrdered.Contains(sub.CustomerId))
            {
                skipped++;
                continue;
            }

            // An order is concretely Home/Pickup; a "Both" customer's auto-orders default to HomeDelivery.
            var orderMode = sub.Customer.DeliveryMode == DeliveryMode.PlantPickup
                ? DeliveryMode.PlantPickup
                : DeliveryMode.HomeDelivery;
            // Plant-pickup orders collect from the plant — no delivery boy / route assignment.
            var deliveryBoyId = orderMode == DeliveryMode.PlantPickup
                ? null
                : await DeliveryBoyResolver.ResolveAsync(_db, sub.Customer.AreaId, ct);

            var order = new Order
            {
                Id = Guid.NewGuid(),
                TenantId = _tenant.TenantId,
                CustomerId = sub.CustomerId,
                AreaId = sub.Customer.AreaId,
                DeliveryBoyId = deliveryBoyId,
                OrderDate = targetDate,
                OrderType = OrderType.Subscription,
                DeliveryMode = orderMode,
                Status = OrderStatus.Pending,
                CreatedBy = _currentUser.UserId
            };
            order.OrderItems.Add(new OrderItem
            {
                Id = Guid.NewGuid(),
                TenantId = _tenant.TenantId,
                OrderId = order.Id,
                ProductId = sub.ProductId,
                Quantity = sub.Quantity,
                UnitRate = sub.RatePerUnit
            });
            order.Delivery = new Delivery
            {
                Id = Guid.NewGuid(),
                TenantId = _tenant.TenantId,
                OrderId = order.Id,
                DeliveryBoyId = deliveryBoyId,
                ScheduledDate = targetDate,
                Status = DeliveryStatus.Pending
            };

            _db.Orders.Add(order);
            alreadyOrdered.Add(sub.CustomerId);   // guard against two subs for the same customer
            created++;
        }

        if (created > 0)
            await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Bulk subscription run for {TargetDate}: created {Created}, skipped {Skipped} of {Considered}",
            targetDate, created, skipped, considered);

        return new BulkCreateResultDto(created, considered, skipped);
    }

    private static bool IsDue(SubscriptionFrequency frequency, DateOnly start, DateOnly target) => frequency switch
    {
        SubscriptionFrequency.Daily => true,
        SubscriptionFrequency.AlternateDay => (target.DayNumber - start.DayNumber) % 2 == 0,
        SubscriptionFrequency.Weekly => target.DayOfWeek == start.DayOfWeek,
        SubscriptionFrequency.Monthly => target.Day == start.Day,
        _ => false   // Custom frequencies are scheduled manually
    };
}
