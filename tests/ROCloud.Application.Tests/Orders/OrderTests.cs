using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Orders.Commands.CancelOrder;
using ROCloud.Application.Features.Orders.Commands.CreateOrder;
using ROCloud.Application.Features.Orders.Dtos;
using ROCloud.Application.Features.Orders.Queries.GetProductionPlan;
using ROCloud.Application.Features.Orders.Queries.GetUpcomingOrders;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Tests.Orders;

public class OrderTests
{
    private static readonly Guid TenantA = Guid.NewGuid();

    private static (AppDbContext Db, TenantContext Ctx) NewDb()
    {
        var ctx = new TenantContext { TenantId = TenantA };
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"orders-{Guid.NewGuid()}").Options, ctx);
        return (db, ctx);
    }

    private sealed class FakeCurrentUser : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public Guid? UserId { get; init; }
        public Guid? TenantId { get; init; }
        public string? Jti => null;
        public DateTime? AccessTokenExpiresAt => null;
        public IReadOnlyCollection<string> Permissions => Array.Empty<string>();
    }

    private static async Task<(Guid CustomerId, Guid ProductId, Guid AreaId, Guid BoyId)> SeedAsync(AppDbContext db)
    {
        var areaId = Guid.NewGuid();
        var boyRoleId = Guid.NewGuid();
        var boyId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        db.Areas.Add(new Area { Id = areaId, TenantId = TenantA, Name = "Sector 1" });
        db.Roles.Add(new Role { Id = boyRoleId, TenantId = TenantA, Name = "DeliveryBoy" });
        db.Users.Add(new User { Id = boyId, TenantId = TenantA, RoleId = boyRoleId, Name = "Boy A", IsActive = true });
        db.Customers.Add(new Customer
        {
            Id = customerId, TenantId = TenantA, Name = "Ravi", Mobile = "9", AreaId = areaId,
            DeliveryMode = DeliveryMode.HomeDelivery
        });
        db.Products.Add(new Product
        {
            Id = productId, TenantId = TenantA, Name = "20L Jar", BottleSize = BottleSize.TwentyL, DefaultRate = 40m
        });
        await db.SaveChangesAsync();
        return (customerId, productId, areaId, boyId);
    }

    [Fact]
    public async Task CreateOrder_AssignsDeliveryBoyByArea()
    {
        var (db, ctx) = NewDb();
        var (customerId, productId, _, boyId) = await SeedAsync(db);

        var handler = new CreateOrderCommandHandler(
            db, ctx, new FakeCurrentUser { UserId = Guid.NewGuid(), TenantId = TenantA },
            NullLogger<CreateOrderCommandHandler>.Instance);

        var orderId = await handler.Handle(
            new CreateOrderCommand(customerId, null, null, null,
                [new CreateOrderItemDto(productId, 2, null)]), CancellationToken.None);

        var order = await db.Orders.FirstAsync(o => o.Id == orderId);
        Assert.Equal(boyId, order.DeliveryBoyId);
        Assert.Equal(OrderStatus.Pending, order.Status);
    }

    [Fact]
    public async Task CreateOrder_CreatesMatchingDelivery()
    {
        var (db, ctx) = NewDb();
        var (customerId, productId, _, boyId) = await SeedAsync(db);

        var handler = new CreateOrderCommandHandler(
            db, ctx, new FakeCurrentUser { UserId = Guid.NewGuid(), TenantId = TenantA },
            NullLogger<CreateOrderCommandHandler>.Instance);

        var orderId = await handler.Handle(
            new CreateOrderCommand(customerId, null, null, null,
                [new CreateOrderItemDto(productId, 1, null)]), CancellationToken.None);

        var delivery = await db.Deliveries.FirstOrDefaultAsync(d => d.OrderId == orderId);
        Assert.NotNull(delivery);
        Assert.Equal(DeliveryStatus.Pending, delivery!.Status);
        Assert.Equal(boyId, delivery.DeliveryBoyId);

        // Unit rate defaulted from the product's default rate (40).
        var item = await db.OrderItems.FirstAsync(i => i.OrderId == orderId);
        Assert.Equal(40m, item.UnitRate);
    }

    [Fact]
    public async Task CreateOrder_PrefersDeliveryBoyAssignedToArea()
    {
        var (db, ctx) = NewDb();
        var (customerId, productId, areaId, _) = await SeedAsync(db);

        // A second delivery boy explicitly assigned to the customer's area (Phase 13).
        var assignedBoyId = Guid.NewGuid();
        var boyRoleId = await db.Roles.Where(r => r.Name == "DeliveryBoy").Select(r => r.Id).FirstAsync();
        db.Users.Add(new User { Id = assignedBoyId, TenantId = TenantA, RoleId = boyRoleId, Name = "Boy B", IsActive = true });
        db.UserAreas.Add(new UserArea { Id = Guid.NewGuid(), TenantId = TenantA, UserId = assignedBoyId, AreaId = areaId });
        await db.SaveChangesAsync();

        var handler = new CreateOrderCommandHandler(
            db, ctx, new FakeCurrentUser { UserId = Guid.NewGuid(), TenantId = TenantA },
            NullLogger<CreateOrderCommandHandler>.Instance);

        var orderId = await handler.Handle(
            new CreateOrderCommand(customerId, null, null, null,
                [new CreateOrderItemDto(productId, 1, null)]), CancellationToken.None);

        var order = await db.Orders.FirstAsync(o => o.Id == orderId);
        Assert.Equal(assignedBoyId, order.DeliveryBoyId);   // the area-assigned boy wins
    }

    [Fact]
    public async Task CancelOrder_NonPending_Throws()
    {
        var (db, _) = NewDb();
        var (customerId, _, _, _) = await SeedAsync(db);
        var order = new Order
        {
            Id = Guid.NewGuid(), TenantId = TenantA, CustomerId = customerId,
            OrderDate = DateOnly.FromDateTime(DateTime.UtcNow), Status = OrderStatus.Delivered
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var handler = new CancelOrderCommandHandler(db);

        await Assert.ThrowsAsync<ValidationException>(() =>
            handler.Handle(new CancelOrderCommand(order.Id), CancellationToken.None));
    }

    // -- Advance / upcoming bookings + production plan ------------------------------------------

    private static async Task SeedOrderAsync(
        AppDbContext db, Guid customerId, Guid productId, DateOnly date,
        int qty, OrderType type = OrderType.Advance, OrderStatus status = OrderStatus.Confirmed)
    {
        var orderId = Guid.NewGuid();
        db.Orders.Add(new Order
        {
            Id = orderId, TenantId = TenantA, CustomerId = customerId,
            OrderDate = date, OrderType = type, Status = status
        });
        db.OrderItems.Add(new OrderItem
        {
            Id = Guid.NewGuid(), TenantId = TenantA, OrderId = orderId,
            ProductId = productId, Quantity = qty, UnitRate = 40m
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetUpcomingOrders_ReturnsOnlyFutureOpenOrders()
    {
        var (db, _) = NewDb();
        var (customerId, productId, _, _) = await SeedAsync(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await SeedOrderAsync(db, customerId, productId, today.AddDays(3), 200);            // future, open → in
        await SeedOrderAsync(db, customerId, productId, today, 10, OrderType.Regular);      // today → out (on board)
        await SeedOrderAsync(db, customerId, productId, today.AddDays(-2), 5, OrderType.Regular); // past → out
        await SeedOrderAsync(db, customerId, productId, today.AddDays(5), 8,
            OrderType.Advance, OrderStatus.Cancelled);                                      // future but cancelled → out

        var handler = new GetUpcomingOrdersQueryHandler(db);
        var result = await handler.Handle(new GetUpcomingOrdersQuery(60), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(today.AddDays(3), result[0].OrderDate);
        Assert.Equal(nameof(OrderType.Advance), result[0].OrderType);
    }

    [Fact]
    public async Task GetProductionPlan_AggregatesQuantityByDateAndProduct()
    {
        var (db, _) = NewDb();
        var (customerId, productId, _, _) = await SeedAsync(db);

        // A second product to prove per-product grouping.
        var product2 = Guid.NewGuid();
        db.Products.Add(new Product
        {
            Id = product2, TenantId = TenantA, Name = "1L Bottle", BottleSize = BottleSize.OneL, DefaultRate = 20m
        });
        await db.SaveChangesAsync();

        var eventDay = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);

        // Two customers booking the same day → the plant must prepare the SUM.
        await SeedOrderAsync(db, customerId, productId, eventDay, 200);
        await SeedOrderAsync(db, customerId, productId, eventDay, 50);
        await SeedOrderAsync(db, customerId, product2, eventDay, 30);
        // A different day, ignored by a tight window.
        await SeedOrderAsync(db, customerId, productId, eventDay.AddDays(3), 99);

        var handler = new GetProductionPlanQueryHandler(db);
        var plan = await handler.Handle(
            new GetProductionPlanQuery(eventDay, eventDay), CancellationToken.None);

        Assert.Single(plan);
        var day = plan[0];
        Assert.Equal(eventDay, day.Date);
        Assert.Equal(280, day.TotalUnits);            // 200 + 50 + 30
        Assert.Equal(3, day.OrderCount);
        // 20L Jar line = 250 (top, ordered by quantity desc), 1L = 30.
        Assert.Equal(productId, day.Lines[0].ProductId);
        Assert.Equal(250, day.Lines[0].TotalQuantity);
        Assert.Equal(2, day.Lines[0].OrderCount);
        Assert.Equal(30, day.Lines[1].TotalQuantity);
        Assert.Equal(3, day.Bookings.Count);
    }
}
