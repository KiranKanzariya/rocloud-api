using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ROCloud.Application.Common;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Deliveries.Commands.UpdateDeliveryStatus;
using ROCloud.Application.Features.Deliveries.Dtos;
using ROCloud.Application.Features.Deliveries.Queries.GetDeliveryBoard;
using ROCloud.Application.Features.Deliveries.Queries.GetMyRoute;
using ROCloud.Application.Features.Customers;
using ROCloud.Application.Services;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;

namespace ROCloud.Application.Tests.Deliveries;

public class DeliveryTests
{
    private static readonly Guid TenantA = Guid.NewGuid();

    private static (AppDbContext Db, TenantContext Ctx) NewDb()
    {
        var ctx = new TenantContext { TenantId = TenantA };
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"deliveries-{Guid.NewGuid()}").Options, ctx);
        return (db, ctx);
    }

    private sealed class FakeCurrentUser : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public Guid? UserId { get; init; }
        public Guid? TenantId { get; init; }
        public string? Jti => null;
        public DateTime? AccessTokenExpiresAt => null;
        public IReadOnlyCollection<string> Permissions { get; init; } = Array.Empty<string>();
    }

    private static readonly string[] CanViewAll = ["Deliveries.View", "Deliveries.Update"];
    private static readonly string[] DeliveryBoyPerms = ["Deliveries.ViewOwn", "Deliveries.Update"];

    private static async Task<(Order Order, Delivery Delivery)> SeedOrderWithDeliveryAsync(
        AppDbContext db, Guid? deliveryBoyId = null, DeliveryStatus status = DeliveryStatus.Pending,
        DeliveryMode deliveryMode = DeliveryMode.HomeDelivery)
    {
        var customerId = Guid.NewGuid();
        db.Customers.Add(new Customer { Id = customerId, TenantId = TenantA, Name = "Cust", Mobile = "9" });
        var order = new Order
        {
            Id = Guid.NewGuid(), TenantId = TenantA, CustomerId = customerId,
            OrderDate = DateOnly.FromDateTime(DateTime.UtcNow), Status = OrderStatus.Pending,
            DeliveryMode = deliveryMode,
            DeliveryBoyId = deliveryBoyId
        };
        var delivery = new Delivery
        {
            Id = Guid.NewGuid(), TenantId = TenantA, OrderId = order.Id,
            DeliveryBoyId = deliveryBoyId, ScheduledDate = order.OrderDate, Status = status
        };
        db.Orders.Add(order);
        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync();
        return (order, delivery);
    }

    [Fact]
    public async Task UpdateDeliveryStatus_OnDelivered_AutoCreatesPayment()
    {
        var (db, ctx) = NewDb();
        var collector = Guid.NewGuid();
        var (order, delivery) = await SeedOrderWithDeliveryAsync(db);

        var currentUser = new FakeCurrentUser { UserId = collector, TenantId = TenantA, Permissions = CanViewAll };
        var handler = new UpdateDeliveryStatusCommandHandler(
            db, ctx, currentUser, new InventoryService(db, ctx, currentUser),
            NullLogger<UpdateDeliveryStatusCommandHandler>.Instance);

        await handler.Handle(new UpdateDeliveryStatusCommand(
            delivery.Id, nameof(DeliveryStatus.Delivered),
            JarsDelivered: 2, JarsReturned: 1, CollectedAmount: 80m,
            PaymentMethod: nameof(PaymentMethod.Cash),
            ProofImageUrl: null, Latitude: null, Longitude: null, Notes: null), CancellationToken.None);

        var payment = await db.Payments.FirstOrDefaultAsync(p => p.OrderId == order.Id);
        Assert.NotNull(payment);
        Assert.Equal(80m, payment!.Amount);
        Assert.Equal(PaymentMethod.Cash, payment.PaymentMethod);
        Assert.Equal(collector, payment.CollectedBy);

        var updatedDelivery = await db.Deliveries.FirstAsync(d => d.Id == delivery.Id);
        Assert.Equal(DeliveryStatus.Delivered, updatedDelivery.Status);
        Assert.NotNull(updatedDelivery.DeliveredAt);

        var updatedOrder = await db.Orders.FirstAsync(o => o.Id == order.Id);
        Assert.Equal(OrderStatus.Delivered, updatedOrder.Status);
    }

    [Fact]
    public async Task UpdateDeliveryStatus_OnFailed_MarksTheOrderFailed_NotLeftInTransit()
    {
        var (db, ctx) = NewDb();
        var (order, delivery) = await SeedOrderWithDeliveryAsync(db, status: DeliveryStatus.InTransit);
        order.Status = OrderStatus.InTransit;   // dispatched
        await db.SaveChangesAsync();

        var currentUser = new FakeCurrentUser { UserId = Guid.NewGuid(), TenantId = TenantA, Permissions = CanViewAll };
        await new UpdateDeliveryStatusCommandHandler(
            db, ctx, currentUser, new InventoryService(db, ctx, currentUser),
            NullLogger<UpdateDeliveryStatusCommandHandler>.Instance).Handle(
            new UpdateDeliveryStatusCommand(delivery.Id, nameof(DeliveryStatus.Failed),
                null, null, null, null, null, null, null, "not home"), CancellationToken.None);

        var updatedOrder = await db.Orders.FirstAsync(o => o.Id == order.Id);
        Assert.Equal(OrderStatus.Failed, updatedOrder.Status);   // was left InTransit before
        var updatedDelivery = await db.Deliveries.FirstAsync(d => d.Id == delivery.Id);
        Assert.Equal(DeliveryStatus.Failed, updatedDelivery.Status);
    }

    [Fact]
    public async Task UpdateDeliveryStatus_RedeliveringAnAlreadyDeliveredStop_DoesNotDoubleCount()
    {
        var (db, ctx) = NewDb();
        var (order, delivery) = await SeedOrderWithDeliveryAsync(db);

        // A product line so the delivery records an Issue movement (the jars-delivered source).
        var productId = Guid.NewGuid();
        db.Products.Add(new Product
        {
            Id = productId, TenantId = TenantA, Name = "20L Jar", BottleSize = BottleSize.TwentyL, DefaultRate = 40m
        });
        db.OrderItems.Add(new OrderItem
        {
            Id = Guid.NewGuid(), TenantId = TenantA, OrderId = order.Id, ProductId = productId,
            Quantity = 2, UnitRate = 40m
        });
        await db.SaveChangesAsync();

        var currentUser = new FakeCurrentUser { UserId = Guid.NewGuid(), TenantId = TenantA, Permissions = CanViewAll };
        UpdateDeliveryStatusCommandHandler NewHandler() => new(
            db, ctx, currentUser, new InventoryService(db, ctx, currentUser),
            NullLogger<UpdateDeliveryStatusCommandHandler>.Instance);
        UpdateDeliveryStatusCommand Deliver() => new(
            delivery.Id, nameof(DeliveryStatus.Delivered),
            JarsDelivered: 2, JarsReturned: 0, CollectedAmount: 80m, PaymentMethod: nameof(PaymentMethod.Cash),
            ProofImageUrl: null, Latitude: null, Longitude: null, Notes: null);

        await NewHandler().Handle(Deliver(), CancellationToken.None);
        await NewHandler().Handle(Deliver(), CancellationToken.None);   // retry / double-tap

        // The second submit is an idempotent no-op — only ONE set of effects, so jars aren't inflated.
        var issued = await db.InventoryMovements
            .Where(m => m.CustomerId == order.CustomerId && m.MovementType == InventoryMovementType.Issue)
            .SumAsync(m => m.Quantity);
        Assert.Equal(2, issued);   // not 4

        Assert.Equal(1, await db.Payments.CountAsync(p => p.OrderId == order.Id));   // not 2
    }

    [Fact]
    public async Task UpdateDeliveryStatus_DeliveringFewerThanOrdered_BillsWhatWasDelivered()
    {
        // Ordered 2, but the customer took only 1 at the door (single-count path). The line's Quantity —
        // which every ledger/invoice read bills off — must follow the delivered jars, while the plan is
        // preserved in OrderedQuantity.
        var (db, ctx) = NewDb();
        var (order, delivery) = await SeedOrderWithDeliveryAsync(db);
        var productId = Guid.NewGuid();
        db.Products.Add(new Product
        {
            Id = productId, TenantId = TenantA, Name = "20L Jar", BottleSize = BottleSize.TwentyL, DefaultRate = 40m
        });
        db.OrderItems.Add(new OrderItem
        {
            Id = Guid.NewGuid(), TenantId = TenantA, OrderId = order.Id, ProductId = productId,
            Quantity = 2, OrderedQuantity = 2, UnitRate = 40m
        });
        await db.SaveChangesAsync();

        var currentUser = new FakeCurrentUser { UserId = Guid.NewGuid(), TenantId = TenantA, Permissions = CanViewAll };
        await new UpdateDeliveryStatusCommandHandler(
            db, ctx, currentUser, new InventoryService(db, ctx, currentUser),
            NullLogger<UpdateDeliveryStatusCommandHandler>.Instance).Handle(
            new UpdateDeliveryStatusCommand(delivery.Id, nameof(DeliveryStatus.Delivered),
                JarsDelivered: 1, JarsReturned: 0, CollectedAmount: 0m, PaymentMethod: null,
                ProofImageUrl: null, Latitude: null, Longitude: null, Notes: null), CancellationToken.None);

        var item = await db.OrderItems.FirstAsync(i => i.OrderId == order.Id);
        Assert.Equal(1, item.Quantity);          // billed = delivered
        Assert.Equal(2, item.OrderedQuantity);   // original plan preserved

        // Ledger charges for the 1 jar handed over (₹40), not the ordered 2 (₹80).
        var balance = await CustomerBalance.ComputeAsync(db, order.CustomerId, CancellationToken.None);
        Assert.Equal(40m, balance);
    }

    [Fact]
    public async Task UpdateDeliveryStatus_DeliveringMoreThanOrdered_BillsWhatWasDelivered()
    {
        // Ordered 2, but the customer wanted 3 (per-item path). The extra jar must be billed — the line's
        // Quantity follows the jars actually handed over, even above what was ordered.
        var (db, ctx) = NewDb();
        var (order, delivery) = await SeedOrderWithDeliveryAsync(db);
        var productId = Guid.NewGuid();
        db.Products.Add(new Product
        {
            Id = productId, TenantId = TenantA, Name = "20L Jar", BottleSize = BottleSize.TwentyL, DefaultRate = 40m
        });
        var orderItemId = Guid.NewGuid();
        db.OrderItems.Add(new OrderItem
        {
            Id = orderItemId, TenantId = TenantA, OrderId = order.Id, ProductId = productId,
            Quantity = 2, OrderedQuantity = 2, UnitRate = 40m
        });
        await db.SaveChangesAsync();

        var currentUser = new FakeCurrentUser { UserId = Guid.NewGuid(), TenantId = TenantA, Permissions = CanViewAll };
        await new UpdateDeliveryStatusCommandHandler(
            db, ctx, currentUser, new InventoryService(db, ctx, currentUser),
            NullLogger<UpdateDeliveryStatusCommandHandler>.Instance).Handle(
            new UpdateDeliveryStatusCommand(delivery.Id, nameof(DeliveryStatus.Delivered),
                JarsDelivered: null, JarsReturned: null, CollectedAmount: 0m, PaymentMethod: null,
                ProofImageUrl: null, Latitude: null, Longitude: null, Notes: null,
                Items: new[] { new DeliveryItemInputDto(orderItemId, JarsDelivered: 3, JarsReturned: 0) }),
            CancellationToken.None);

        var item = await db.OrderItems.FirstAsync(i => i.OrderId == order.Id);
        Assert.Equal(3, item.Quantity);          // billed = delivered (above the ordered 2)
        Assert.Equal(2, item.OrderedQuantity);

        var balance = await CustomerBalance.ComputeAsync(db, order.CustomerId, CancellationToken.None);
        Assert.Equal(120m, balance);             // 3 × ₹40
    }

    [Fact]
    public async Task UpdateDeliveryStatus_OtherDelivery_AddsOffOrderProductAsBilledLine()
    {
        // Ordered a 20L; customer refused it (delivered 0) and took an 18L instead. The 18L isn't on the
        // order, so it must be added as a new billed line + Issue movement — while the 20L bills nothing.
        var (db, ctx) = NewDb();
        var (order, delivery) = await SeedOrderWithDeliveryAsync(db);

        var p20 = Guid.NewGuid();
        var p18 = Guid.NewGuid();
        db.Products.Add(new Product { Id = p20, TenantId = TenantA, Name = "20L Jar", BottleSize = BottleSize.TwentyL, DefaultRate = 40m });
        db.Products.Add(new Product { Id = p18, TenantId = TenantA, Name = "18L Jar", BottleSize = BottleSize.EighteenL, DefaultRate = 35m });
        var orderItemId = Guid.NewGuid();
        db.OrderItems.Add(new OrderItem
        {
            Id = orderItemId, TenantId = TenantA, OrderId = order.Id, ProductId = p20,
            Quantity = 1, OrderedQuantity = 1, UnitRate = 40m
        });
        await db.SaveChangesAsync();

        var currentUser = new FakeCurrentUser { UserId = Guid.NewGuid(), TenantId = TenantA, Permissions = CanViewAll };
        await new UpdateDeliveryStatusCommandHandler(
            db, ctx, currentUser, new InventoryService(db, ctx, currentUser),
            NullLogger<UpdateDeliveryStatusCommandHandler>.Instance).Handle(
            new UpdateDeliveryStatusCommand(delivery.Id, nameof(DeliveryStatus.Delivered),
                JarsDelivered: null, JarsReturned: null, CollectedAmount: 0m, PaymentMethod: null,
                ProofImageUrl: null, Latitude: null, Longitude: null, Notes: null,
                Items: new[] { new DeliveryItemInputDto(orderItemId, JarsDelivered: 0, JarsReturned: 0) },
                OtherDeliveries: new[] { new OtherDeliveryInputDto(p18, Quantity: 1, UnitRate: null) }),
            CancellationToken.None);

        // The 20L line now bills nothing (delivered 0); a new 18L line was added at its default rate.
        var items = await db.OrderItems.Where(i => i.OrderId == order.Id).ToListAsync();
        Assert.Equal(2, items.Count);
        Assert.Equal(0, items.Single(i => i.ProductId == p20).Quantity);
        var eighteen = items.Single(i => i.ProductId == p18);
        Assert.Equal(1, eighteen.Quantity);
        Assert.Equal(0, eighteen.OrderedQuantity);   // never ordered → "added at delivery"
        Assert.Equal(35m, eighteen.UnitRate);        // product default rate (no explicit rate supplied)

        // Ledger charges only the 18L actually handed over (₹35), not the refused 20L.
        var balance = await CustomerBalance.ComputeAsync(db, order.CustomerId, CancellationToken.None);
        Assert.Equal(35m, balance);

        // Inventory issued the 18L.
        Assert.True(await db.InventoryMovements.AnyAsync(
            m => m.ProductId == p18 && m.MovementType == InventoryMovementType.Issue && m.Quantity == 1));
    }

    [Fact]
    public async Task UpdateDeliveryStatus_DeliveredWithoutCollection_CreatesNoPayment()
    {
        var (db, ctx) = NewDb();
        var (order, delivery) = await SeedOrderWithDeliveryAsync(db);

        var currentUser = new FakeCurrentUser { UserId = Guid.NewGuid(), TenantId = TenantA, Permissions = CanViewAll };
        var handler = new UpdateDeliveryStatusCommandHandler(
            db, ctx, currentUser, new InventoryService(db, ctx, currentUser),
            NullLogger<UpdateDeliveryStatusCommandHandler>.Instance);

        await handler.Handle(new UpdateDeliveryStatusCommand(
            delivery.Id, nameof(DeliveryStatus.Delivered),
            JarsDelivered: 1, JarsReturned: 1, CollectedAmount: 0m,
            PaymentMethod: null, ProofImageUrl: null, Latitude: null, Longitude: null, Notes: null),
            CancellationToken.None);

        Assert.False(await db.Payments.AnyAsync(p => p.OrderId == order.Id));
    }

    [Fact]
    public async Task UpdateDeliveryStatus_FutureScheduledDate_IsRejected()
    {
        var (db, ctx) = NewDb();
        var (_, delivery) = await SeedOrderWithDeliveryAsync(db);

        // A stop generated ahead of time (e.g. tonight's rollover creating tomorrow's deliveries).
        delivery.ScheduledDate = AppTimeZone.Today(DateTime.UtcNow).AddDays(1);
        await db.SaveChangesAsync();

        var currentUser = new FakeCurrentUser { UserId = Guid.NewGuid(), TenantId = TenantA, Permissions = CanViewAll };
        var handler = new UpdateDeliveryStatusCommandHandler(
            db, ctx, currentUser, new InventoryService(db, ctx, currentUser),
            NullLogger<UpdateDeliveryStatusCommandHandler>.Instance);

        // Even a same-instant "Delivered" is rejected because the stop isn't due until tomorrow.
        await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(
            new UpdateDeliveryStatusCommand(delivery.Id, nameof(DeliveryStatus.Delivered),
                JarsDelivered: 1, JarsReturned: 0, CollectedAmount: 0m,
                PaymentMethod: null, ProofImageUrl: null, Latitude: null, Longitude: null, Notes: null),
            CancellationToken.None));

        // The stop stays untouched.
        var unchanged = await db.Deliveries.FirstAsync(d => d.Id == delivery.Id);
        Assert.Equal(DeliveryStatus.Pending, unchanged.Status);
    }

    [Fact]
    public async Task UpdateDeliveryStatus_ClosedAfterItsDay_StampsJarMovementsOnTheDeliveryDay()
    {
        // A round entered the next morning (or a backdated order — UpdateOrder keeps ScheduledDate in
        // sync with OrderDate). The jars moved on the stop's own day, so the movement must be booked
        // there: otherwise "jars delivered this month" credits the month it was typed in, and a stop
        // from the 30th lands in the next month's figure.
        var (db, ctx) = NewDb();
        var (order, delivery) = await SeedOrderWithDeliveryAsync(db);

        var productId = Guid.NewGuid();
        db.Products.Add(new Product
        {
            Id = productId, TenantId = TenantA, Name = "20L Jar", BottleSize = BottleSize.TwentyL, DefaultRate = 40m
        });
        db.OrderItems.Add(new OrderItem
        {
            Id = Guid.NewGuid(), TenantId = TenantA, OrderId = order.Id, ProductId = productId,
            Quantity = 2, OrderedQuantity = 2, UnitRate = 40m
        });
        var deliveredOn = AppTimeZone.Today(DateTime.UtcNow).AddDays(-3);
        delivery.ScheduledDate = deliveredOn;
        await db.SaveChangesAsync();

        var currentUser = new FakeCurrentUser { UserId = Guid.NewGuid(), TenantId = TenantA, Permissions = CanViewAll };
        await new UpdateDeliveryStatusCommandHandler(
            db, ctx, currentUser, new InventoryService(db, ctx, currentUser),
            NullLogger<UpdateDeliveryStatusCommandHandler>.Instance).Handle(
            new UpdateDeliveryStatusCommand(delivery.Id, nameof(DeliveryStatus.Delivered),
                JarsDelivered: 2, JarsReturned: 1, CollectedAmount: 0m, PaymentMethod: null,
                ProofImageUrl: null, Latitude: null, Longitude: null, Notes: null), CancellationToken.None);

        // Both legs of the visit (jars out and empties back) are booked on the delivery day.
        var movements = await db.InventoryMovements.Where(m => m.CustomerId == order.CustomerId).ToListAsync();
        Assert.Equal(2, movements.Count);
        Assert.All(movements, m => Assert.Equal(deliveredOn, AppTimeZone.Today(m.CreatedAt)));

        // DeliveredAt still records when it was actually entered — the audit trail isn't rewritten.
        var updated = await db.Deliveries.FirstAsync(d => d.Id == delivery.Id);
        Assert.True(updated.DeliveredAt > DateTime.UtcNow.AddMinutes(-5));
    }

    [Fact]
    public async Task UpdateDeliveryStatus_SameDayStop_KeepsTheRealClockTimeOnMovements()
    {
        // The normal case: closed on its own day. Nothing is rewritten — the movement keeps "now", so
        // today's jar history stays in true chronological order rather than collapsing onto midday.
        var (db, ctx) = NewDb();
        var (order, delivery) = await SeedOrderWithDeliveryAsync(db);

        var productId = Guid.NewGuid();
        db.Products.Add(new Product
        {
            Id = productId, TenantId = TenantA, Name = "20L Jar", BottleSize = BottleSize.TwentyL, DefaultRate = 40m
        });
        db.OrderItems.Add(new OrderItem
        {
            Id = Guid.NewGuid(), TenantId = TenantA, OrderId = order.Id, ProductId = productId,
            Quantity = 2, OrderedQuantity = 2, UnitRate = 40m
        });
        delivery.ScheduledDate = AppTimeZone.Today(DateTime.UtcNow);
        await db.SaveChangesAsync();

        var before = DateTime.UtcNow;
        var currentUser = new FakeCurrentUser { UserId = Guid.NewGuid(), TenantId = TenantA, Permissions = CanViewAll };
        await new UpdateDeliveryStatusCommandHandler(
            db, ctx, currentUser, new InventoryService(db, ctx, currentUser),
            NullLogger<UpdateDeliveryStatusCommandHandler>.Instance).Handle(
            new UpdateDeliveryStatusCommand(delivery.Id, nameof(DeliveryStatus.Delivered),
                JarsDelivered: 2, JarsReturned: 0, CollectedAmount: 0m, PaymentMethod: null,
                ProofImageUrl: null, Latitude: null, Longitude: null, Notes: null), CancellationToken.None);

        var issue = await db.InventoryMovements.FirstAsync(
            m => m.CustomerId == order.CustomerId && m.MovementType == InventoryMovementType.Issue);
        Assert.InRange(issue.CreatedAt, before, DateTime.UtcNow);
    }

    [Fact]
    public async Task UpdateDeliveryStatus_DeliveryBoy_CannotUpdateAnotherBoysStop()
    {
        var (db, ctx) = NewDb();
        var me = Guid.NewGuid();
        var (_, delivery) = await SeedOrderWithDeliveryAsync(db, deliveryBoyId: Guid.NewGuid()); // assigned to someone else

        var currentUser = new FakeCurrentUser { UserId = me, TenantId = TenantA, Permissions = DeliveryBoyPerms };
        var handler = new UpdateDeliveryStatusCommandHandler(
            db, ctx, currentUser, new InventoryService(db, ctx, currentUser),
            NullLogger<UpdateDeliveryStatusCommandHandler>.Instance);

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => handler.Handle(
            new UpdateDeliveryStatusCommand(delivery.Id, nameof(DeliveryStatus.InTransit),
                null, null, null, null, null, null, null, null), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateDeliveryStatus_DeliveryBoy_CanUpdateOwnStop()
    {
        var (db, ctx) = NewDb();
        var me = Guid.NewGuid();
        var (_, delivery) = await SeedOrderWithDeliveryAsync(db, deliveryBoyId: me);

        var currentUser = new FakeCurrentUser { UserId = me, TenantId = TenantA, Permissions = DeliveryBoyPerms };
        var handler = new UpdateDeliveryStatusCommandHandler(
            db, ctx, currentUser, new InventoryService(db, ctx, currentUser),
            NullLogger<UpdateDeliveryStatusCommandHandler>.Instance);

        await handler.Handle(new UpdateDeliveryStatusCommand(
            delivery.Id, nameof(DeliveryStatus.InTransit),
            null, null, null, null, null, null, null, null), CancellationToken.None);

        var updated = await db.Deliveries.FirstAsync(d => d.Id == delivery.Id);
        Assert.Equal(DeliveryStatus.InTransit, updated.Status);
    }

    [Fact]
    public async Task GetDeliveryDetail_DeliveryBoy_CanReadOwnStop()
    {
        var (db, _) = NewDb();
        var me = Guid.NewGuid();
        var (_, delivery) = await SeedOrderWithDeliveryAsync(db, deliveryBoyId: me, status: DeliveryStatus.Delivered);

        var handler = new Application.Features.Deliveries.Queries.GetDeliveryDetail.GetDeliveryDetailQueryHandler(
            db, new FakeCurrentUser { UserId = me, TenantId = TenantA, Permissions = DeliveryBoyPerms });

        var detail = await handler.Handle(
            new Application.Features.Deliveries.Queries.GetDeliveryDetail.GetDeliveryDetailQuery(delivery.Id),
            CancellationToken.None);

        Assert.Equal(delivery.Id, detail.Id);
    }

    [Fact]
    public async Task GetDeliveryDetail_DeliveryBoy_CannotReadAnotherBoysStop()
    {
        var (db, _) = NewDb();
        var (_, delivery) = await SeedOrderWithDeliveryAsync(
            db, deliveryBoyId: Guid.NewGuid(), status: DeliveryStatus.Delivered);   // someone else's stop

        var handler = new Application.Features.Deliveries.Queries.GetDeliveryDetail.GetDeliveryDetailQueryHandler(
            db, new FakeCurrentUser { UserId = Guid.NewGuid(), TenantId = TenantA, Permissions = DeliveryBoyPerms });

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => handler.Handle(
            new Application.Features.Deliveries.Queries.GetDeliveryDetail.GetDeliveryDetailQuery(delivery.Id),
            CancellationToken.None));
    }

    [Fact]
    public async Task GetMyRoute_AsDeliveryBoy_ReturnsOnlyOwnDeliveries()
    {
        var (db, _) = NewDb();
        var me = Guid.NewGuid();
        var other = Guid.NewGuid();
        await SeedOrderWithDeliveryAsync(db, deliveryBoyId: me);
        await SeedOrderWithDeliveryAsync(db, deliveryBoyId: me);
        await SeedOrderWithDeliveryAsync(db, deliveryBoyId: other);

        var handler = new GetMyRouteQueryHandler(
            db, new FakeCurrentUser { UserId = me, TenantId = TenantA });

        var route = await handler.Handle(new GetMyRouteQuery(new DeliveryFilterDto()), CancellationToken.None);

        Assert.Equal(2, route.Count);
        Assert.All(route, d => Assert.Equal(me, d.DeliveryBoyId));
    }

    [Fact]
    public async Task GetBoard_GroupsByStatus()
    {
        var (db, _) = NewDb();
        await SeedOrderWithDeliveryAsync(db, status: DeliveryStatus.Pending);
        await SeedOrderWithDeliveryAsync(db, status: DeliveryStatus.InTransit);
        await SeedOrderWithDeliveryAsync(db, status: DeliveryStatus.Delivered);
        await SeedOrderWithDeliveryAsync(db, status: DeliveryStatus.Delivered);

        var handler = new GetDeliveryBoardQueryHandler(db);
        var board = await handler.Handle(new GetDeliveryBoardQuery(new DeliveryFilterDto()), CancellationToken.None);

        Assert.Single(board.Pending);
        Assert.Single(board.InTransit);
        Assert.Equal(2, board.Delivered.Count);
    }

    [Fact]
    public async Task GetBoard_Pickups_ListAwaitingPickupBeforeCompletedAndFailed()
    {
        var (db, _) = NewDb();
        // Seed out of order on purpose — delivered/failed first, awaiting last.
        await SeedOrderWithDeliveryAsync(db, status: DeliveryStatus.Delivered, deliveryMode: DeliveryMode.PlantPickup);
        await SeedOrderWithDeliveryAsync(db, status: DeliveryStatus.Failed, deliveryMode: DeliveryMode.PlantPickup);
        await SeedOrderWithDeliveryAsync(db, status: DeliveryStatus.Pending, deliveryMode: DeliveryMode.PlantPickup);

        var handler = new GetDeliveryBoardQueryHandler(db);
        var board = await handler.Handle(new GetDeliveryBoardQuery(new DeliveryFilterDto()), CancellationToken.None);

        // Awaiting pickup (Pending) is what still needs action, so it must surface first.
        Assert.Equal(3, board.Pickups.Count);
        Assert.Equal(nameof(DeliveryStatus.Pending), board.Pickups[0].Status);
        Assert.Equal(nameof(DeliveryStatus.Failed), board.Pickups[^1].Status);

        // Pickups stay out of the home-delivery route columns entirely.
        Assert.Empty(board.Pending);
        Assert.Empty(board.Delivered);
    }

    [Fact]
    public async Task GetDeliveryProductTotals_SumsTodaysDeliveriesByProduct()
    {
        var (db, _) = NewDb();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var p20 = Guid.NewGuid();
        var p18 = Guid.NewGuid();
        db.Products.Add(new Product { Id = p20, TenantId = TenantA, Name = "20L Jar", BottleSize = BottleSize.TwentyL });
        db.Products.Add(new Product { Id = p18, TenantId = TenantA, Name = "18L Jar", BottleSize = BottleSize.EighteenL });

        async Task Stop(DateOnly day, Guid productId, int qty)
        {
            var customerId = Guid.NewGuid();
            db.Customers.Add(new Customer { Id = customerId, TenantId = TenantA, Name = "C", Mobile = "9" });
            var orderId = Guid.NewGuid();
            db.Orders.Add(new Order { Id = orderId, TenantId = TenantA, CustomerId = customerId, OrderDate = day, Status = OrderStatus.Pending });
            db.OrderItems.Add(new OrderItem { Id = Guid.NewGuid(), TenantId = TenantA, OrderId = orderId, ProductId = productId, Quantity = qty, UnitRate = 40m });
            db.Deliveries.Add(new Delivery { Id = Guid.NewGuid(), TenantId = TenantA, OrderId = orderId, ScheduledDate = day, Status = DeliveryStatus.Pending });
            await db.SaveChangesAsync();
        }
        await Stop(today, p20, 3);
        await Stop(today, p20, 2);   // 20L today totals 5
        await Stop(today, p18, 1);   // 18L today totals 1
        await Stop(today.AddDays(-1), p20, 9);   // yesterday — must be excluded

        var handler = new Application.Features.Deliveries.Queries.GetDeliveryProductTotals.GetDeliveryProductTotalsQueryHandler(db);
        var totals = await handler.Handle(
            new Application.Features.Deliveries.Queries.GetDeliveryProductTotals.GetDeliveryProductTotalsQuery(
                new DeliveryFilterDto { Date = today }),
            CancellationToken.None);

        Assert.Equal(2, totals.Count);
        Assert.Equal("20L Jar", totals[0].ProductName);   // ordered by quantity desc
        Assert.Equal(5, totals[0].Quantity);              // 3 + 2, yesterday's 9 excluded
        Assert.Equal("18L Jar", totals[1].ProductName);
        Assert.Equal(1, totals[1].Quantity);
    }

    [Fact]
    public async Task GetDeliveryDetail_OtherEmpties_ResolveProductNameAndSize()
    {
        // An empty of a DIFFERENT size returned during a delivery is recorded as a Return movement for
        // a product NOT on the order. Its name/size must still resolve — else the row renders as "()".
        var (db, _) = NewDb();
        var (_, delivery) = await SeedOrderWithDeliveryAsync(db, status: DeliveryStatus.Delivered);

        // A product that is NOT on the order (the "other empty").
        var otherProductId = Guid.NewGuid();
        db.Products.Add(new Product
        {
            Id = otherProductId, TenantId = TenantA, Name = "18L Jar", BottleSize = BottleSize.EighteenL
        });
        db.InventoryMovements.Add(new InventoryMovement
        {
            Id = Guid.NewGuid(), TenantId = TenantA, ProductId = otherProductId,
            OrderId = delivery.OrderId, MovementType = InventoryMovementType.Return, Quantity = 2
        });
        await db.SaveChangesAsync();

        var handler = new Application.Features.Deliveries.Queries.GetDeliveryDetail.GetDeliveryDetailQueryHandler(
            db, new FakeCurrentUser { UserId = Guid.NewGuid(), TenantId = TenantA, Permissions = CanViewAll });
        var detail = await handler.Handle(
            new Application.Features.Deliveries.Queries.GetDeliveryDetail.GetDeliveryDetailQuery(delivery.Id),
            CancellationToken.None);

        var other = Assert.Single(detail.OtherReturns);
        Assert.Equal("18L Jar", other.ProductName);   // was blank before the fix
        Assert.Equal("18L", other.BottleSize);
        Assert.Equal(2, other.Quantity);
    }
}
