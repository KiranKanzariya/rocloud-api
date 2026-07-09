using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Deliveries.Commands.UpdateDeliveryStatus;
using ROCloud.Application.Features.Inventory.Commands.AddInventoryMovement;
using ROCloud.Application.Features.Inventory.Commands.ReconcileInventory;
using ROCloud.Application.Services;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;

namespace ROCloud.Application.Tests.Inventory;

public class InventoryTests
{
    private static readonly Guid TenantA = Guid.NewGuid();

    private static (AppDbContext Db, TenantContext Ctx) NewDb()
    {
        var ctx = new TenantContext { TenantId = TenantA };
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"inventory-{Guid.NewGuid()}").Options, ctx);
        return (db, ctx);
    }

    private sealed class FakeCurrentUser : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public Guid? UserId { get; init; }
        public Guid? TenantId { get; init; }
        public string? Jti => null;
        public DateTime? AccessTokenExpiresAt => null;
        // These tests act as an owner/manager (can update any delivery), so the ownership guard passes.
        public IReadOnlyCollection<string> Permissions => new[] { "Deliveries.View" };
    }

    private static async Task<(Guid ProductId, Delivery Delivery)> SeedDeliverableAsync(AppDbContext db)
    {
        var productId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        db.Products.Add(new Product
        {
            Id = productId, TenantId = TenantA, Name = "20L Jar", BottleSize = BottleSize.TwentyL, DefaultRate = 40m
        });
        db.Customers.Add(new Customer { Id = customerId, TenantId = TenantA, Name = "Cust", Mobile = "9" });
        var order = new Order
        {
            Id = Guid.NewGuid(), TenantId = TenantA, CustomerId = customerId,
            OrderDate = DateOnly.FromDateTime(DateTime.UtcNow), Status = OrderStatus.Pending
        };
        order.OrderItems.Add(new OrderItem
        {
            Id = Guid.NewGuid(), TenantId = TenantA, OrderId = order.Id, ProductId = productId, Quantity = 2, UnitRate = 40m
        });
        var delivery = new Delivery
        {
            Id = Guid.NewGuid(), TenantId = TenantA, OrderId = order.Id,
            ScheduledDate = order.OrderDate, Status = DeliveryStatus.Pending
        };
        db.Orders.Add(order);
        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync();
        return (productId, delivery);
    }

    private static UpdateDeliveryStatusCommandHandler NewDeliveryHandler(
        AppDbContext db, TenantContext ctx, ICurrentUserService user)
        => new(db, ctx, user, new InventoryService(db, ctx, user),
            NullLogger<UpdateDeliveryStatusCommandHandler>.Instance);

    [Fact]
    public async Task DeliveryCompleted_UpdatesIssuedStock()
    {
        var (db, ctx) = NewDb();
        var (productId, delivery) = await SeedDeliverableAsync(db);
        var user = new FakeCurrentUser { UserId = Guid.NewGuid(), TenantId = TenantA };

        await NewDeliveryHandler(db, ctx, user).Handle(new UpdateDeliveryStatusCommand(
            delivery.Id, nameof(DeliveryStatus.Delivered),
            JarsDelivered: 5, JarsReturned: 0, CollectedAmount: 0m,
            PaymentMethod: null, ProofImageUrl: null, Latitude: null, Longitude: null, Notes: null),
            CancellationToken.None);

        var inv = await db.Inventories.FirstAsync(i => i.ProductId == productId);
        Assert.Equal(5, inv.IssuedStock);
        Assert.Equal(0, inv.ReturnedStock);

        Assert.True(await db.InventoryMovements.AnyAsync(
            m => m.ProductId == productId && m.MovementType == InventoryMovementType.Issue && m.Quantity == 5));
    }

    [Fact]
    public async Task DeliveryWithReturns_UpdatesReturnedStock()
    {
        var (db, ctx) = NewDb();
        var (productId, delivery) = await SeedDeliverableAsync(db);
        var user = new FakeCurrentUser { UserId = Guid.NewGuid(), TenantId = TenantA };

        await NewDeliveryHandler(db, ctx, user).Handle(new UpdateDeliveryStatusCommand(
            delivery.Id, nameof(DeliveryStatus.Delivered),
            JarsDelivered: 5, JarsReturned: 3, CollectedAmount: 0m,
            PaymentMethod: null, ProofImageUrl: null, Latitude: null, Longitude: null, Notes: null),
            CancellationToken.None);

        var inv = await db.Inventories.FirstAsync(i => i.ProductId == productId);
        Assert.Equal(2, inv.IssuedStock);   // 5 issued - 3 returned
        Assert.Equal(3, inv.ReturnedStock);

        Assert.True(await db.InventoryMovements.AnyAsync(
            m => m.ProductId == productId && m.MovementType == InventoryMovementType.Return && m.Quantity == 3));
    }

    [Fact]
    public async Task AddMovement_Restock_IncreasesTotal()
    {
        var (db, ctx) = NewDb();
        var (productId, _) = await SeedDeliverableAsync(db);
        var user = new FakeCurrentUser { UserId = Guid.NewGuid(), TenantId = TenantA };

        var handler = new AddInventoryMovementCommandHandler(db, ctx, user);
        await handler.Handle(new AddInventoryMovementCommand(
            productId, nameof(InventoryMovementType.Restock), 100, null, null, "Bought 100 jars"),
            CancellationToken.None);

        var inv = await db.Inventories.FirstAsync(i => i.ProductId == productId);
        Assert.Equal(100, inv.TotalStock);
        Assert.Equal(100, inv.TotalStock - inv.IssuedStock - inv.DamagedStock);   // available
    }

    [Fact]
    public async Task InventoryReconciliation_FixesDiscrepancy()
    {
        var (db, ctx) = NewDb();
        var (productId, _) = await SeedDeliverableAsync(db);
        var user = new FakeCurrentUser { UserId = Guid.NewGuid(), TenantId = TenantA };
        var add = new AddInventoryMovementCommandHandler(db, ctx, user);

        // Ledger: +100 restock, 30 issued, 10 returned. Expected: total 100, issued 20, returned 10.
        await add.Handle(new AddInventoryMovementCommand(productId, nameof(InventoryMovementType.Restock), 100, null, null, null), CancellationToken.None);
        await add.Handle(new AddInventoryMovementCommand(productId, nameof(InventoryMovementType.Issue), 30, null, null, null), CancellationToken.None);
        await add.Handle(new AddInventoryMovementCommand(productId, nameof(InventoryMovementType.Return), 10, null, null, null), CancellationToken.None);

        // Corrupt the cached totals.
        var inv = await db.Inventories.FirstAsync(i => i.ProductId == productId);
        inv.TotalStock = 999;
        inv.IssuedStock = -5;
        inv.ReturnedStock = 0;
        inv.DamagedStock = 42;
        await db.SaveChangesAsync();

        var result = await new ReconcileInventoryCommandHandler(db, ctx)
            .Handle(new ReconcileInventoryCommand(), CancellationToken.None);

        Assert.Equal(1, result.Discrepancies);
        var fixedInv = await db.Inventories.FirstAsync(i => i.ProductId == productId);
        Assert.Equal(100, fixedInv.TotalStock);
        Assert.Equal(20, fixedInv.IssuedStock);
        Assert.Equal(10, fixedInv.ReturnedStock);
        Assert.Equal(0, fixedInv.DamagedStock);
    }
}
