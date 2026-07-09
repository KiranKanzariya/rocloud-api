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
        AppDbContext db, Guid? deliveryBoyId = null, DeliveryStatus status = DeliveryStatus.Pending)
    {
        var customerId = Guid.NewGuid();
        db.Customers.Add(new Customer { Id = customerId, TenantId = TenantA, Name = "Cust", Mobile = "9" });
        var order = new Order
        {
            Id = Guid.NewGuid(), TenantId = TenantA, CustomerId = customerId,
            OrderDate = DateOnly.FromDateTime(DateTime.UtcNow), Status = OrderStatus.Pending,
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
}
