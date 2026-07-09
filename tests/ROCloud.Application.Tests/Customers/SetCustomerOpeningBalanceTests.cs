using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Customers;
using ROCloud.Application.Features.Customers.Commands.ClearCustomerOpeningBalance;
using ROCloud.Application.Features.Customers.Commands.SetCustomerOpeningBalance;
using ROCloud.Application.Features.Customers.Queries.GetCustomerJarBalance;
using ROCloud.Application.Features.Customers.Queries.GetCustomerOpeningBalance;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Tests.Customers;

public class SetCustomerOpeningBalanceTests
{
    private static readonly Guid TenantA = Guid.NewGuid();

    private static (AppDbContext Db, TenantContext Ctx) NewDb()
    {
        var ctx = new TenantContext { TenantId = TenantA };
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"opening-{Guid.NewGuid()}").Options, ctx);
        return (db, ctx);
    }

    private sealed class FakeCurrentUser : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public Guid? UserId { get; init; } = Guid.NewGuid();
        public Guid? TenantId { get; init; } = TenantA;
        public string? Jti => null;
        public DateTime? AccessTokenExpiresAt => null;
        public IReadOnlyCollection<string> Permissions => Array.Empty<string>();
    }

    private static async Task<(Guid CustomerId, Guid ProductId)> SeedAsync(AppDbContext db)
    {
        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        db.Customers.Add(new Customer { Id = customerId, TenantId = TenantA, Name = "Ramesh", Mobile = "9876543210" });
        db.Products.Add(new Product { Id = productId, TenantId = TenantA, Name = "20L Jar", BottleSize = BottleSize.TwentyL });
        await db.SaveChangesAsync();
        return (customerId, productId);
    }

    private static SetCustomerOpeningBalanceCommandHandler NewHandler(AppDbContext db, TenantContext ctx) =>
        new(db, ctx, new FakeCurrentUser());

    private static DateOnly Cutover => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task Seeds_jars_and_dues()
    {
        var (db, ctx) = NewDb();
        var (customerId, productId) = await SeedAsync(db);

        await NewHandler(db, ctx).Handle(new SetCustomerOpeningBalanceCommand(
            customerId, Cutover, new[] { new OpeningJarInputDto(productId, 3) }, OpeningDues: 450m, Note: null),
            CancellationToken.None);

        // Jars held = 3 (one Issue movement scoped to the customer).
        var jars = await new GetCustomerJarBalanceQueryHandler(db)
            .Handle(new GetCustomerJarBalanceQuery(customerId), CancellationToken.None);
        Assert.Equal(3, Assert.Single(jars).Outstanding);

        // Money owed = 450 (one Sent invoice, no payments).
        var balance = await CustomerBalance.ComputeAsync(db, customerId, CancellationToken.None);
        Assert.Equal(450m, balance);

        var invoice = Assert.Single(await db.Invoices.ToListAsync());
        Assert.Equal(InvoiceStatus.Sent, invoice.Status);
        Assert.Equal(450m, invoice.TotalAmount);
        Assert.Null(invoice.PeriodFrom);  // must not "cover" real delivered orders
        Assert.StartsWith(SetCustomerOpeningBalanceCommand.Marker, invoice.Notes);
    }

    [Fact]
    public async Task Negative_dues_records_advance_as_credit()
    {
        var (db, ctx) = NewDb();
        var (customerId, _) = await SeedAsync(db);

        await NewHandler(db, ctx).Handle(new SetCustomerOpeningBalanceCommand(
            customerId, Cutover, Array.Empty<OpeningJarInputDto>(), OpeningDues: -200m, Note: null),
            CancellationToken.None);

        Assert.Empty(await db.Invoices.ToListAsync());
        var payment = Assert.Single(await db.Payments.ToListAsync());
        Assert.Equal(200m, payment.Amount);
        Assert.Null(payment.InvoiceId);

        // Advance → negative (credit) balance.
        var balance = await CustomerBalance.ComputeAsync(db, customerId, CancellationToken.None);
        Assert.Equal(-200m, balance);
    }

    [Fact]
    public async Task Second_run_is_blocked()
    {
        var (db, ctx) = NewDb();
        var (customerId, productId) = await SeedAsync(db);
        var cmd = new SetCustomerOpeningBalanceCommand(
            customerId, Cutover, new[] { new OpeningJarInputDto(productId, 2) }, 0m, null);

        await NewHandler(db, ctx).Handle(cmd, CancellationToken.None);

        await Assert.ThrowsAsync<ValidationException>(() =>
            NewHandler(db, ctx).Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task Unknown_product_is_rejected()
    {
        var (db, ctx) = NewDb();
        var (customerId, _) = await SeedAsync(db);

        await Assert.ThrowsAsync<ValidationException>(() =>
            NewHandler(db, ctx).Handle(new SetCustomerOpeningBalanceCommand(
                customerId, Cutover, new[] { new OpeningJarInputDto(Guid.NewGuid(), 1) }, 0m, null),
                CancellationToken.None));
    }

    [Fact]
    public async Task Clear_reverses_everything_and_allows_reseed()
    {
        var (db, ctx) = NewDb();
        var (customerId, productId) = await SeedAsync(db);
        await NewHandler(db, ctx).Handle(new SetCustomerOpeningBalanceCommand(
            customerId, Cutover, new[] { new OpeningJarInputDto(productId, 3) }, 450m, null), CancellationToken.None);

        await new ClearCustomerOpeningBalanceCommandHandler(db)
            .Handle(new ClearCustomerOpeningBalanceCommand(customerId), CancellationToken.None);

        // Jars rolled back, invoice gone, balance back to zero.
        var jars = await new GetCustomerJarBalanceQueryHandler(db)
            .Handle(new GetCustomerJarBalanceQuery(customerId), CancellationToken.None);
        Assert.Empty(jars);
        Assert.Empty(await db.Invoices.ToListAsync());
        Assert.Equal(0m, await CustomerBalance.ComputeAsync(db, customerId, CancellationToken.None));
        Assert.Equal(0, (await db.Inventories.FirstAsync(i => i.ProductId == productId)).IssuedStock);

        // After clearing, the customer can be seeded again (guard released).
        await NewHandler(db, ctx).Handle(new SetCustomerOpeningBalanceCommand(
            customerId, Cutover, Array.Empty<OpeningJarInputDto>(), 100m, null), CancellationToken.None);
        Assert.Equal(100m, await CustomerBalance.ComputeAsync(db, customerId, CancellationToken.None));
    }

    [Fact]
    public async Task Status_query_reflects_set_then_cleared()
    {
        var (db, ctx) = NewDb();
        var (customerId, productId) = await SeedAsync(db);

        var before = await new GetCustomerOpeningBalanceQueryHandler(db)
            .Handle(new GetCustomerOpeningBalanceQuery(customerId), CancellationToken.None);
        Assert.False(before.IsSet);

        await NewHandler(db, ctx).Handle(new SetCustomerOpeningBalanceCommand(
            customerId, Cutover, new[] { new OpeningJarInputDto(productId, 3) }, 450m, null), CancellationToken.None);

        var after = await new GetCustomerOpeningBalanceQueryHandler(db)
            .Handle(new GetCustomerOpeningBalanceQuery(customerId), CancellationToken.None);
        Assert.True(after.IsSet);
        Assert.Equal(450m, after.Dues);
        Assert.Equal(3, Assert.Single(after.Jars).Quantity);

        await new ClearCustomerOpeningBalanceCommandHandler(db)
            .Handle(new ClearCustomerOpeningBalanceCommand(customerId), CancellationToken.None);
        var cleared = await new GetCustomerOpeningBalanceQueryHandler(db)
            .Handle(new GetCustomerOpeningBalanceQuery(customerId), CancellationToken.None);
        Assert.False(cleared.IsSet);
    }
}
