using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ROCloud.Application.Common;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Deliveries.Dtos;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Features.Deliveries.Commands.UpdateDeliveryStatus;

/// <summary>
/// Updates a delivery's status (used by delivery boys from mobile). On Delivered it
/// records the proof-of-delivery fields, syncs the order, and auto-creates a Payment.
/// </summary>
public sealed record UpdateDeliveryStatusCommand(
    Guid Id,
    string Status,
    int? JarsDelivered,
    int? JarsReturned,
    decimal? CollectedAmount,
    string? PaymentMethod,
    string? ProofImageUrl,
    decimal? Latitude,
    decimal? Longitude,
    string? Notes,
    IReadOnlyList<DeliveryItemInputDto>? Items = null,
    // Empties returned for products NOT on this order (e.g. a 20L returned during an 18L delivery).
    IReadOnlyList<OtherReturnInputDto>? OtherReturns = null,
    // Products handed over that were NOT on this order (e.g. an 18L given when the ordered 20L was
    // refused). Each becomes a new order line so it is billed and inventoried like any other item.
    IReadOnlyList<OtherDeliveryInputDto>? OtherDeliveries = null) : IRequest;

public class UpdateDeliveryStatusCommandValidator : AbstractValidator<UpdateDeliveryStatusCommand>
{
    public UpdateDeliveryStatusCommandValidator()
    {
        RuleFor(c => c.Status)
            .Must(v => Enum.GetNames<DeliveryStatus>().Contains(v))
            .WithMessage("Invalid delivery status.");
        RuleFor(c => c.JarsDelivered).GreaterThanOrEqualTo(0).When(c => c.JarsDelivered.HasValue);
        RuleFor(c => c.JarsReturned).GreaterThanOrEqualTo(0).When(c => c.JarsReturned.HasValue);
        RuleForEach(c => c.Items).ChildRules(i =>
        {
            i.RuleFor(x => x.OrderItemId).NotEmpty();
            i.RuleFor(x => x.JarsDelivered).GreaterThanOrEqualTo(0);
            i.RuleFor(x => x.JarsReturned).GreaterThanOrEqualTo(0);
        }).When(c => c.Items is not null);
        RuleForEach(c => c.OtherReturns).ChildRules(i =>
        {
            i.RuleFor(x => x.ProductId).NotEmpty();
            i.RuleFor(x => x.Quantity).GreaterThan(0);
        }).When(c => c.OtherReturns is not null);
        RuleForEach(c => c.OtherDeliveries).ChildRules(i =>
        {
            i.RuleFor(x => x.ProductId).NotEmpty();
            i.RuleFor(x => x.Quantity).GreaterThan(0);
            i.RuleFor(x => x.UnitRate).GreaterThanOrEqualTo(0).When(x => x.UnitRate.HasValue);
        }).When(c => c.OtherDeliveries is not null);
        RuleFor(c => c.CollectedAmount).GreaterThanOrEqualTo(0).When(c => c.CollectedAmount.HasValue);
        RuleFor(c => c.PaymentMethod)
            .Must(v => v is null || Enum.GetNames<PaymentMethod>().Contains(v))
            .WithMessage("Invalid payment method.");
    }
}

public class UpdateDeliveryStatusCommandHandler : IRequestHandler<UpdateDeliveryStatusCommand>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUserService _currentUser;
    private readonly IInventoryService _inventory;
    private readonly ILogger<UpdateDeliveryStatusCommandHandler> _logger;

    public UpdateDeliveryStatusCommandHandler(
        IAppDbContext db, ITenantContext tenant, ICurrentUserService currentUser,
        IInventoryService inventory, ILogger<UpdateDeliveryStatusCommandHandler> logger)
    {
        _db = db;
        _tenant = tenant;
        _currentUser = currentUser;
        _inventory = inventory;
        _logger = logger;
    }

    public async Task Handle(UpdateDeliveryStatusCommand request, CancellationToken ct)
    {
        var delivery = await _db.Deliveries
            .Include(d => d.Order)
            .FirstOrDefaultAsync(d => d.Id == request.Id, ct)
            ?? throw new NotFoundException("Delivery", request.Id);

        // A delivery boy (Deliveries.ViewOwn but not Deliveries.View) may only update stops assigned
        // to them; owners/managers (who can view the whole board) may update any stop.
        var canUpdateAny = _currentUser.Permissions.Contains("Deliveries.View");
        if (!canUpdateAny && delivery.DeliveryBoyId != _currentUser.UserId)
            throw new ForbiddenAccessException();

        // A stop can't be actioned before its scheduled day. The daily rollover generates tomorrow's
        // deliveries tonight, and they surface on tomorrow's board — but marking one Delivered/Skipped
        // today is meaningless. "Today" is the app timezone (App:TimeZone, default IST; guide §1) so
        // early-morning stops (before 05:30 IST, when UTC is still yesterday) aren't wrongly blocked.
        var today = AppTimeZone.Today(DateTime.UtcNow);
        if (delivery.ScheduledDate > today)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["scheduledDate"] =
                    [$"This delivery is scheduled for {delivery.ScheduledDate:dd MMM yyyy} and can't be updated before then."]
            });

        var status = Enum.Parse<DeliveryStatus>(request.Status);
        var alreadyDelivered = delivery.Status == DeliveryStatus.Delivered;

        // Plant-pickup orders have no delivery boy / route — the customer collects from the plant,
        // so there is no "in transit" leg. Such stops go straight Pending → Delivered.
        if (status == DeliveryStatus.InTransit && delivery.Order?.DeliveryMode == DeliveryMode.PlantPickup)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["status"] = ["Plant-pickup orders are marked delivered directly, not in transit."]
            });

        delivery.Status = status;
        if (request.Notes is not null) delivery.Notes = request.Notes;

        switch (status)
        {
            case DeliveryStatus.InTransit:
                if (delivery.Order is { } inTransitOrder)
                    inTransitOrder.Status = OrderStatus.InTransit;
                break;

            case DeliveryStatus.Delivered:
                // Idempotent: a retried or double-tapped submit (the delivery boy's phone re-sends a
                // timed-out request) must NOT create a second set of Issue movements + DeliveryItems +
                // doorstep payment — that double-counts the customer's jars delivered. Apply the effects
                // only on the FIRST transition into Delivered; a repeat is a no-op success. (The UI
                // already renders a Delivered stop read-only, so there is no legitimate re-submit.)
                if (!alreadyDelivered)
                    await ApplyDeliveredAsync(delivery, request, today, ct);
                break;

            case DeliveryStatus.Failed:
                // Mirror the outcome onto the order so it doesn't keep reading "InTransit" on the order
                // list / board / filters. Failed ≠ Delivered, so it stays out of every dues/invoice
                // calculation (those all key off Delivered) — a failed drop owes nothing, correctly.
                if (delivery.Order is { } failedOrder)
                    failedOrder.Status = OrderStatus.Failed;
                break;

            case DeliveryStatus.Skipped:
                break;
        }

        // The delivery/inventory movement and the re-settlement it causes are one fact — commit both
        // or neither, so a sync failure can't leave stock moved and doorstep cash recorded while the
        // customer's invoices still read unpaid. Guarded for the non-relational in-memory test provider.
        await using var tx = _db.IsRelational ? await _db.BeginTransactionAsync(ct) : null;

        await _db.SaveChangesAsync(ct);

        // Delivering changes what the customer owes in two ways at once: the order becomes an
        // obligation, and any doorstep cash becomes a payment. Re-settle their invoices against both.
        if (delivery.Order?.CustomerId is { } deliveredFor)
            await Payments.InvoiceAllocationSync.SyncAsync(_db, deliveredFor, ct);

        if (tx is not null) await tx.CommitAsync(ct);
    }

    private async Task ApplyDeliveredAsync(
        Delivery delivery, UpdateDeliveryStatusCommand request, DateOnly today, CancellationToken ct)
    {
        // The business day the jars moved. A stop closed after its scheduled day — a round entered the
        // next morning, or a backdated order (UpdateOrder keeps ScheduledDate in sync with OrderDate) —
        // must book its jar movements on the delivery's own day, so "jars delivered this month" counts
        // them where the order is billed. A same-day stop (the normal case) stays null so the movement
        // keeps the real clock time and today's movement list stays in true chronological order.
        var movedOn = delivery.ScheduledDate < today ? delivery.ScheduledDate : (DateOnly?)null;

        delivery.DeliveredAt = DateTime.UtcNow;
        delivery.CollectedAmount = request.CollectedAmount ?? 0m;
        delivery.PaymentMethod = request.PaymentMethod is { } pm ? Enum.Parse<PaymentMethod>(pm) : null;
        if (request.ProofImageUrl is not null) delivery.ProofImageUrl = request.ProofImageUrl;
        delivery.Latitude = request.Latitude;
        delivery.Longitude = request.Longitude;

        if (delivery.Order is { } order)
        {
            order.Status = OrderStatus.Delivered;

            // Auto-create a Payment when money was collected on the doorstep.
            if (delivery.CollectedAmount is { } amount && amount > 0)
            {
                if (delivery.PaymentMethod is null or PaymentMethod.None)
                    throw new ValidationException(new Dictionary<string, string[]>
                    {
                        ["paymentMethod"] = ["A payment method is required when an amount is collected."]
                    });

                _db.Payments.Add(new Payment
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenant.TenantId,
                    CustomerId = order.CustomerId,
                    OrderId = order.Id,
                    Amount = amount,
                    PaymentMethod = delivery.PaymentMethod.Value,
                    Status = PaymentStatus.Completed,
                    CollectedBy = _currentUser.UserId,
                    PaidAt = DateTime.UtcNow
                });
            }
        }

        // Update the bottle float per product (guide §9): jars_delivered → issued, jars_returned →
        // returned. Per-item quantities (multi-item orders) are preferred; a single delivered/returned
        // count is still accepted (legacy / single-item) and applied to the order's primary product.
        var customerId = delivery.Order?.CustomerId;
        if (request.Items is { Count: > 0 })
            await ApplyPerItemAsync(delivery, request.Items, customerId, movedOn, ct);
        else
            await ApplySingleCountAsync(delivery, request, customerId, movedOn, ct);

        // Empties returned for products that aren't on this order (e.g. a 20L brought back during an
        // 18L delivery). These reduce that product's customer balance but don't belong to any order item,
        // so they're recorded purely as customer-scoped Return movements (visible in the return history).
        if (request.OtherReturns is { Count: > 0 })
            foreach (var r in request.OtherReturns)
                await _inventory.RecordReturnAsync(r.ProductId, r.Quantity, delivery.OrderId, customerId, movedOn, ct);

        // Products handed over that weren't on the order (e.g. an 18L given when the ordered 20L was
        // refused). Each is added to the order as a new line so it is billed and inventoried like any
        // other item — the mirror of OtherReturns for deliveries.
        if (request.OtherDeliveries is { Count: > 0 })
            await ApplyOtherDeliveriesAsync(delivery, request.OtherDeliveries, customerId, movedOn, ct);
    }

    private async Task ApplyOtherDeliveriesAsync(
        Delivery delivery, IReadOnlyList<OtherDeliveryInputDto> extras, Guid? customerId,
        DateOnly? movedOn, CancellationToken ct)
    {
        var productIds = extras.Select(e => e.ProductId).Distinct().ToList();
        var rates = await _db.Products
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.DefaultRate })
            .ToDictionaryAsync(p => p.Id, p => p.DefaultRate, ct);

        var missing = productIds.Where(id => !rates.ContainsKey(id)).ToList();
        if (missing.Count > 0)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["otherDeliveries"] = ["An off-order delivered product does not exist."]
            });

        var extraDelivered = 0;
        foreach (var extra in extras)
        {
            // A brand-new order line for the substituted/added product. ordered_quantity = 0 marks it as
            // "added at delivery" (never ordered); quantity = jars handed over drives the bill. total_amount
            // is the stored generated column, so the charge derives itself — every billing read just works.
            var orderItem = new OrderItem
            {
                Id = Guid.NewGuid(),
                TenantId = _tenant.TenantId,
                OrderId = delivery.OrderId,
                ProductId = extra.ProductId,
                Quantity = extra.Quantity,
                OrderedQuantity = 0,
                UnitRate = extra.UnitRate ?? rates[extra.ProductId]
            };
            _db.OrderItems.Add(orderItem);

            await _inventory.RecordIssueAsync(extra.ProductId, extra.Quantity, delivery.OrderId, customerId, movedOn, ct);

            _db.DeliveryItems.Add(new DeliveryItem
            {
                Id = Guid.NewGuid(),
                TenantId = _tenant.TenantId,
                DeliveryId = delivery.Id,
                OrderItemId = orderItem.Id,
                ProductId = extra.ProductId,
                JarsDelivered = extra.Quantity,
                JarsReturned = 0
            });

            extraDelivered += extra.Quantity;
        }

        // Keep the header jars-delivered total inclusive of the off-order items (set by the per-item /
        // single-count path just above).
        delivery.JarsDelivered = (delivery.JarsDelivered ?? 0) + extraDelivered;
    }

    private async Task ApplyPerItemAsync(
        Delivery delivery, IReadOnlyList<DeliveryItemInputDto> items, Guid? customerId,
        DateOnly? movedOn, CancellationToken ct)
    {
        // Tracked (not projected) so each line's Quantity can be rewritten to the jars actually
        // delivered — see below. Resolves each input to a real order item of THIS order.
        var orderItems = await _db.OrderItems
            .Where(i => i.OrderId == delivery.OrderId)
            .ToDictionaryAsync(i => i.Id, ct);

        var totalDelivered = 0;
        var totalReturned = 0;

        foreach (var item in items)
        {
            if (!orderItems.TryGetValue(item.OrderItemId, out var orderItem))
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["items"] = ["A delivery line does not belong to this order."]
                });

            var productId = orderItem.ProductId;

            // Bill what was delivered: a line's charge is quantity * unit_rate, and every ledger / invoice
            // read sums Quantity — so setting Quantity to the jars actually handed over makes the customer
            // pay for exactly what they received, whether that's more or fewer than ordered. OrderedQuantity
            // still holds the plan. A line the boy leaves out of `items` keeps its ordered quantity.
            orderItem.Quantity = item.JarsDelivered;

            if (item.JarsDelivered > 0)
                await _inventory.RecordIssueAsync(productId, item.JarsDelivered, delivery.OrderId, customerId, movedOn, ct);
            if (item.JarsReturned > 0)
                await _inventory.RecordReturnAsync(productId, item.JarsReturned, delivery.OrderId, customerId, movedOn, ct);

            _db.DeliveryItems.Add(new DeliveryItem
            {
                Id = Guid.NewGuid(),
                TenantId = _tenant.TenantId,
                DeliveryId = delivery.Id,
                OrderItemId = item.OrderItemId,
                ProductId = productId,
                JarsDelivered = item.JarsDelivered,
                JarsReturned = item.JarsReturned
            });

            totalDelivered += item.JarsDelivered;
            totalReturned += item.JarsReturned;
        }

        // Keep the header counts as the totals so lists/board still show a jar summary.
        delivery.JarsDelivered = totalDelivered;
        delivery.JarsReturned = totalReturned;
    }

    private async Task ApplySingleCountAsync(
        Delivery delivery, UpdateDeliveryStatusCommand request, Guid? customerId,
        DateOnly? movedOn, CancellationToken ct)
    {
        delivery.JarsDelivered = request.JarsDelivered ?? 0;
        delivery.JarsReturned = request.JarsReturned ?? 0;

        // Tracked so the primary line's Quantity can be rewritten to what was delivered (below).
        var orderItems = await _db.OrderItems
            .Where(i => i.OrderId == delivery.OrderId)
            .OrderBy(i => i.Id)
            .ToListAsync(ct);

        var primary = orderItems.FirstOrDefault();
        if (primary is null)
        {
            _logger.LogWarning(
                "Delivery {DeliveryId} has no order items; skipping inventory update.", delivery.Id);
            return;
        }

        // Bill what was delivered (single-count path): a single-item order's charge follows the delivered
        // jar count. Only when a count is explicitly supplied — a caller that marks delivered without a jar
        // count keeps the ordered quantity (old behaviour). A multi-item order reached via this legacy path
        // has an ambiguous split, so its quantities are left untouched (the per-item path carries the breakdown).
        if (orderItems.Count == 1 && request.JarsDelivered is { } billed)
            primary.Quantity = billed;

        var pid = primary.ProductId;
        if (delivery.JarsDelivered is { } delivered && delivered > 0)
            await _inventory.RecordIssueAsync(pid, delivered, delivery.OrderId, customerId, movedOn, ct);
        if (delivery.JarsReturned is { } returned && returned > 0)
            await _inventory.RecordReturnAsync(pid, returned, delivery.OrderId, customerId, movedOn, ct);
    }
}
