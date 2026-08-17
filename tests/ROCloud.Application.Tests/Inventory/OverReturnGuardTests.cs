using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Inventory.Commands.AddInventoryMovement;
using ROCloud.Application.Features.Inventory.Commands.RecordCustomerReturn;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Tests.Inventory;

/// <summary>
/// More jars cannot come back than went out.
///
/// <para>Before this guard a return of any size succeeded for any customer, and the damage was
/// invisible rather than merely wrong: <c>GetCustomerJarBalance</c> filters to
/// <c>Outstanding &gt; 0</c>, so an over-returned product DISAPPEARS from the customer's jar balance
/// instead of showing negative, while <c>Inventory.IssuedStock</c> silently drops below zero. The
/// plant's float and the deposit figures were then wrong with nothing on any screen to say so.</para>
///
/// <para>The refusal has to point somewhere useful, because the legitimate version of this exists: a
/// jar issued before ROCloud, whose Issue was never recorded. That is what the opening balance is
/// for, and the message says so.</para>
/// </summary>
public class OverReturnGuardTests
{
    private static readonly Guid TenantA = Guid.NewGuid();

    private static (AppDbContext Db, TenantContext Ctx) NewDb()
    {
        var ctx = new TenantContext { TenantId = TenantA };
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"overreturn-{Guid.NewGuid()}").Options, ctx);
        return (db, ctx);
    }

    private sealed class FakeCurrentUser : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public Guid? UserId { get; init; } = Guid.NewGuid();
        public Guid? TenantId { get; init; }
        public string? Jti => null;
        public DateTime? AccessTokenExpiresAt => null;
        public IReadOnlyCollection<string> Permissions { get; init; } = ["Inventory.Manage"];
    }

    /// <summary>Seeds a customer, a product, and <paramref name="issued"/> jars in their hands.</summary>
    private static async Task<(Guid CustomerId, Guid ProductId)> SeedAsync(AppDbContext db, int issued)
    {
        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        db.Products.Add(new Product
        {
            Id = productId, TenantId = TenantA, Name = "20L Jar", BottleSize = BottleSize.TwentyL, DefaultRate = 40m,
        });
        db.Customers.Add(new Customer
        {
            Id = customerId, TenantId = TenantA, Name = "Kamlesh Parshotam", Mobile = "9978551402",
        });
        if (issued > 0)
            db.InventoryMovements.Add(new InventoryMovement
            {
                Id = Guid.NewGuid(), TenantId = TenantA, ProductId = productId, CustomerId = customerId,
                MovementType = InventoryMovementType.Issue, Quantity = issued,
            });
        await db.SaveChangesAsync();
        return (customerId, productId);
    }

    private static RecordCustomerReturnCommandHandler Returns(AppDbContext db, TenantContext ctx) =>
        new(db, ctx, new FakeCurrentUser { TenantId = TenantA }, new Auth.FakeAppSettings());

    private static AddInventoryMovementCommandHandler Movements(AppDbContext db, TenantContext ctx) =>
        new(db, ctx, new FakeCurrentUser { TenantId = TenantA });

    // ── The dedicated returns endpoint ──────────────────────────────────────────

    [Fact]
    public async Task ReturningWhatTheyHoldIsAllowed()
    {
        var (db, ctx) = NewDb();
        var (customerId, productId) = await SeedAsync(db, issued: 5);

        await Returns(db, ctx).Handle(
            new RecordCustomerReturnCommand(customerId, productId, 5, null, null), CancellationToken.None);

        Assert.Single(await db.InventoryMovements
            .Where(m => m.MovementType == InventoryMovementType.Return).ToListAsync());
    }

    [Fact]
    public async Task ReturningOneMoreThanTheyHoldIsRefused()
    {
        // The boundary, because off-by-one here is the difference between a working feature and a
        // float that drifts one jar at a time.
        var (db, ctx) = NewDb();
        var (customerId, productId) = await SeedAsync(db, issued: 5);

        var error = await Assert.ThrowsAsync<ValidationException>(() =>
            Returns(db, ctx).Handle(
                new RecordCustomerReturnCommand(customerId, productId, 6, null, null), CancellationToken.None));

        Assert.Contains("only holding 5", error.Errors["quantity"][0]);
        Assert.Empty(await db.InventoryMovements
            .Where(m => m.MovementType == InventoryMovementType.Return).ToListAsync());
    }

    [Fact]
    public async Task ACustomerHoldingNothingCannotReturnAnything()
    {
        // The report that started this: any bottle, any quantity, any customer.
        var (db, ctx) = NewDb();
        var (customerId, productId) = await SeedAsync(db, issued: 0);

        var error = await Assert.ThrowsAsync<ValidationException>(() =>
            Returns(db, ctx).Handle(
                new RecordCustomerReturnCommand(customerId, productId, 1, null, null), CancellationToken.None));

        // Names the customer and points at the tool for genuinely older jars, rather than "invalid".
        Assert.Contains("Kamlesh Parshotam", error.Errors["quantity"][0]);
        Assert.Contains("opening balance", error.Errors["quantity"][0]);
    }

    [Fact]
    public async Task EarlierReturnsCountAgainstTheCeiling()
    {
        // 5 out, 3 already back — only 2 remain. A guard that looked at issues alone would let the
        // same five jars come back twice.
        var (db, ctx) = NewDb();
        var (customerId, productId) = await SeedAsync(db, issued: 5);
        await Returns(db, ctx).Handle(
            new RecordCustomerReturnCommand(customerId, productId, 3, null, null), CancellationToken.None);

        await Assert.ThrowsAsync<ValidationException>(() =>
            Returns(db, ctx).Handle(
                new RecordCustomerReturnCommand(customerId, productId, 3, null, null), CancellationToken.None));

        await Returns(db, ctx).Handle(
            new RecordCustomerReturnCommand(customerId, productId, 2, null, null), CancellationToken.None);
    }

    [Fact]
    public async Task ADamagedReturnCountsAgainstTheCeilingToo()
    {
        // A broken jar left the customer's hands as surely as a good one, so it must both consume the
        // ceiling and be limited by it.
        var (db, ctx) = NewDb();
        var (customerId, productId) = await SeedAsync(db, issued: 2);

        await Returns(db, ctx).Handle(
            new RecordCustomerReturnCommand(customerId, productId, 2, null, null, Damaged: true),
            CancellationToken.None);

        await Assert.ThrowsAsync<ValidationException>(() =>
            Returns(db, ctx).Handle(
                new RecordCustomerReturnCommand(customerId, productId, 1, null, null), CancellationToken.None));
    }

    [Fact]
    public async Task AnotherCustomersJarsDoNotCount()
    {
        // Holdings are per customer. Pooling them would let one customer return another's jars.
        var (db, ctx) = NewDb();
        var (_, productId) = await SeedAsync(db, issued: 9);
        var strangerId = Guid.NewGuid();
        db.Customers.Add(new Customer { Id = strangerId, TenantId = TenantA, Name = "Nirav Vyas", Mobile = "9" });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            Returns(db, ctx).Handle(
                new RecordCustomerReturnCommand(strangerId, productId, 1, null, null), CancellationToken.None));
    }

    // ── The generic movements endpoint — the other door ─────────────────────────

    [Fact]
    public async Task TheMovementsEndpointIsGuardedToo()
    {
        // This is what the owner portal's per-row return posts to. Guarding only the dedicated
        // endpoint would leave the float reachable through the back door.
        var (db, ctx) = NewDb();
        var (customerId, productId) = await SeedAsync(db, issued: 1);

        await Assert.ThrowsAsync<ValidationException>(() =>
            Movements(db, ctx).Handle(
                new AddInventoryMovementCommand(
                    productId, nameof(InventoryMovementType.Return), 4, null, customerId, null),
                CancellationToken.None));
    }

    [Fact]
    public async Task AWarehouseMovementWithNoCustomerIsUntouched()
    {
        // Restocks, adjustments and breakage inside the plant have no customer and no ceiling — the
        // guard must not reach past customer-scoped movements.
        var (db, ctx) = NewDb();
        var (_, productId) = await SeedAsync(db, issued: 0);

        await Movements(db, ctx).Handle(
            new AddInventoryMovementCommand(
                productId, nameof(InventoryMovementType.Restock), 500, null, null, null),
            CancellationToken.None);

        var inv = await db.Inventories.FirstAsync(i => i.ProductId == productId);
        Assert.Equal(500, inv.TotalStock);
    }
}
