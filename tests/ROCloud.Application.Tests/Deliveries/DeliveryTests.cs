using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ROCloud.Application.Common;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Deliveries.Commands.UpdateDeliveryStatus;
using ROCloud.Application.Features.Deliveries.Dtos;
using ROCloud.Application.Features.Deliveries.Queries.GetDeliveryBoard;
using ROCloud.Application.Features.Deliveries.Queries.GetMyRoute;
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

        var handler = new Application.Features.Deliveries.Queries.GetDeliveryDetail.GetDeliveryDetailQueryHandler(db);
        var detail = await handler.Handle(
            new Application.Features.Deliveries.Queries.GetDeliveryDetail.GetDeliveryDetailQuery(delivery.Id),
            CancellationToken.None);

        var other = Assert.Single(detail.OtherReturns);
        Assert.Equal("18L Jar", other.ProductName);   // was blank before the fix
        Assert.Equal("18L", other.BottleSize);
        Assert.Equal(2, other.Quantity);
    }
}
