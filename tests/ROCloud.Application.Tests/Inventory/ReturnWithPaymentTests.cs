using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Inventory.Commands.RecordCustomerReturn;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;

namespace ROCloud.Application.Tests.Inventory;

/// <summary>
/// A customer at the counter hands back jars AND pays, in one moment.
///
/// <para>Recording that as two requests is how a counter visit ends up half-written: the jars logged,
/// the cash not, and the customer chased through Money in for what he already handed over. The reverse
/// leaves his jar count wrong, which is what the deposit and recovery figures rest on. Neither half is
/// recoverable afterwards, because nothing links the two records.</para>
///
/// <para>The delivery modal has always taken both together. This brings the counter into line.</para>
/// </summary>
public class ReturnWithPaymentTests
{
    private static readonly Guid TenantA = Guid.NewGuid();

    private static (AppDbContext Db, TenantContext Ctx) NewDb()
    {
        var ctx = new TenantContext { TenantId = TenantA };
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"return-pay-{Guid.NewGuid()}").Options, ctx);
        return (db, ctx);
    }

    private sealed class FakeCurrentUser : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public Guid? UserId { get; init; } = Guid.NewGuid();
        public Guid? TenantId { get; init; }
        public string? Jti => null;
        public DateTime? AccessTokenExpiresAt => null;
        public IReadOnlyCollection<string> Permissions { get; init; } = ["Inventory.Manage", "Payments.Collect"];
    }

    private static async Task<(Guid CustomerId, Guid ProductId)> SeedAsync(AppDbContext db)
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
        await db.SaveChangesAsync();
        return (customerId, productId);
    }

    private static RecordCustomerReturnCommandHandler Handler(
        AppDbContext db, TenantContext ctx, ICurrentUserService? user = null) =>
        new(db, ctx, user ?? new FakeCurrentUser { TenantId = TenantA }, new Auth.FakeAppSettings());

    [Fact]
    public async Task JarsAndCashAreBothRecordedFromOneRequest()
    {
        var (db, ctx) = NewDb();
        var (customerId, productId) = await SeedAsync(db);

        var result = await Handler(db, ctx).Handle(
            new RecordCustomerReturnCommand(customerId, productId, 2, null, null,
                CollectedAmount: 300m, PaymentMethod: nameof(PaymentMethod.Cash)),
            CancellationToken.None);

        Assert.NotNull(result.PaymentId);
        Assert.Equal(300m, result.CollectedAmount);

        var movement = await db.InventoryMovements.FirstAsync();
        Assert.Equal(InventoryMovementType.Return, movement.MovementType);
        Assert.Equal(2, movement.Quantity);

        var payment = await db.Payments.FirstAsync();
        Assert.Equal(300m, payment.Amount);
        Assert.Equal(PaymentMethod.Cash, payment.PaymentMethod);
        Assert.Equal(PaymentStatus.Completed, payment.Status);
        Assert.Equal(customerId, payment.CustomerId);
    }

    [Fact]
    public async Task WithoutAnAmountItIsExactlyTheReturnItAlwaysWas()
    {
        // The customer who brings jars back but does not pay must still be one action, not a form with
        // a field he has to be talked past.
        var (db, ctx) = NewDb();
        var (customerId, productId) = await SeedAsync(db);

        var result = await Handler(db, ctx).Handle(
            new RecordCustomerReturnCommand(customerId, productId, 2, null, null),
            CancellationToken.None);

        Assert.Null(result.PaymentId);
        Assert.Equal(0m, result.CollectedAmount);
        Assert.Empty(await db.Payments.ToListAsync());
        Assert.Single(await db.InventoryMovements.ToListAsync());
    }

    [Fact]
    public async Task AZeroAmountIsTreatedAsNoPayment()
    {
        // The modal defaults the field to 0 rather than prefilling the balance, so 0 arrives constantly
        // and must never produce a ₹0 payment row cluttering the customer's ledger.
        var (db, ctx) = NewDb();
        var (customerId, productId) = await SeedAsync(db);

        await Handler(db, ctx).Handle(
            new RecordCustomerReturnCommand(customerId, productId, 1, null, null,
                CollectedAmount: 0m, PaymentMethod: nameof(PaymentMethod.Cash)),
            CancellationToken.None);

        Assert.Empty(await db.Payments.ToListAsync());
    }

    [Fact]
    public async Task ThePaymentReportsOnTheSameDayAsTheReturn()
    {
        // They are one event. A backdated counter visit whose jars land on Tuesday and whose cash lands
        // on Thursday is two different stories in the reports.
        var (db, ctx) = NewDb();
        var (customerId, productId) = await SeedAsync(db);
        var twoDaysAgo = AppTimeZone.Today(DateTime.UtcNow).AddDays(-2);

        await Handler(db, ctx).Handle(
            new RecordCustomerReturnCommand(customerId, productId, 1, twoDaysAgo, null,
                CollectedAmount: 120m, PaymentMethod: nameof(PaymentMethod.UPI)),
            CancellationToken.None);

        var movement = await db.InventoryMovements.FirstAsync();
        var payment = await db.Payments.FirstAsync();
        Assert.Equal(twoDaysAgo, AppTimeZone.Today(movement.CreatedAt));
        Assert.Equal(twoDaysAgo, AppTimeZone.Today(payment.PaidAt));
    }

    [Fact]
    public async Task AStockOnlyRoleCannotBookMoneyThroughTheReturnsDoor()
    {
        // The endpoint is gated on Inventory.Manage. Money needs the money permission too, or a
        // stock-only role could record payments on a screen they can see and a screen they cannot.
        var (db, ctx) = NewDb();
        var (customerId, productId) = await SeedAsync(db);
        var stockOnly = new FakeCurrentUser { TenantId = TenantA, Permissions = ["Inventory.Manage"] };

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            Handler(db, ctx, stockOnly).Handle(
                new RecordCustomerReturnCommand(customerId, productId, 1, null, null,
                    CollectedAmount: 300m, PaymentMethod: nameof(PaymentMethod.Cash)),
                CancellationToken.None));

        // And nothing at all was written — not even the jars, which would otherwise leave the counter
        // visit half-recorded in the very way this feature exists to prevent.
        Assert.Empty(await db.Payments.ToListAsync());
        Assert.Empty(await db.InventoryMovements.ToListAsync());
    }

    [Fact]
    public async Task AStockOnlyRoleCanStillRecordAPlainReturn()
    {
        var (db, ctx) = NewDb();
        var (customerId, productId) = await SeedAsync(db);
        var stockOnly = new FakeCurrentUser { TenantId = TenantA, Permissions = ["Inventory.Manage"] };

        await Handler(db, ctx, stockOnly).Handle(
            new RecordCustomerReturnCommand(customerId, productId, 2, null, null),
            CancellationToken.None);

        Assert.Single(await db.InventoryMovements.ToListAsync());
    }

    [Theory]
    [InlineData(300, null, false)]                  // money with no method — how was it paid?
    [InlineData(300, "None", false)]
    [InlineData(300, "Cash", true)]
    [InlineData(null, null, true)]                  // a plain return needs no method
    [InlineData(0, null, true)]
    public void AMethodIsRequiredOnlyWhenMoneyChangedHands(int? amount, string? method, bool valid)
    {
        var result = new RecordCustomerReturnCommandValidator().Validate(
            new RecordCustomerReturnCommand(Guid.NewGuid(), Guid.NewGuid(), 1, null, null,
                amount is null ? null : amount.Value, method));

        Assert.Equal(valid, result.IsValid);
    }

    [Fact]
    public void ARequestWithNoJarsIsStillRejected()
    {
        // Payment-only belongs on Collect payment, which sits on the same screen. Two doors to one
        // outcome is worse than the asymmetry, and a "return" of zero jars is a contradiction.
        var result = new RecordCustomerReturnCommandValidator().Validate(
            new RecordCustomerReturnCommand(Guid.NewGuid(), Guid.NewGuid(), 0, null, null,
                CollectedAmount: 300m, PaymentMethod: nameof(PaymentMethod.Cash)));

        Assert.False(result.IsValid);
    }
}
