using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Features.Subscription.Commands.InitiateSubscription;
using ROCloud.Domain.Entities.Platform;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;

namespace ROCloud.Application.Tests.Subscriptions;

/// <summary>Revenue-leak fix: a paid upgrade now creates a real Razorpay order.</summary>
public class InitiateSubscriptionTests
{
    private static (AppDbContext Db, TenantContext Ctx) NewDb()
    {
        var ctx = new TenantContext { TenantId = Guid.NewGuid() };
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"init-{Guid.NewGuid()}").Options, ctx);
        return (db, ctx);
    }

    private static async Task SeedAsync(AppDbContext db, Guid tenantId,
        SubscriptionDiscountType discType = SubscriptionDiscountType.None, decimal discVal = 0m,
        PlanType planType = PlanType.Pro)
    {
        var plan = new Plan { Id = Guid.NewGuid(), Name = planType.ToString(), PlanType = planType, MonthlyPrice = 999m, YearlyPrice = 9990m, IsActive = true };
        db.Plans.Add(plan);
        db.Tenants.Add(new Tenant
        {
            Id = tenantId, PlanId = plan.Id, Name = "Co", Subdomain = "co",
            OwnerName = "O", OwnerEmail = "o@x.com", OwnerMobile = "9",
            SubscriptionDiscountType = discType, SubscriptionDiscountValue = discVal
        });
        await db.SaveChangesAsync();
    }

    private static InitiateSubscriptionCommandHandler Handler(AppDbContext db, TenantContext ctx, FakeRazorpayService rp)
        => new(db, rp, ctx);

    [Fact]
    public async Task PaidUpgrade_LiveKeys_CreatesOrder()
    {
        var (db, ctx) = NewDb();
        await SeedAsync(db, ctx.TenantId);

        var dto = await Handler(db, ctx, new FakeRazorpayService { Configured = true, CreatedOrderId = "order_new" })
            .Handle(new InitiateSubscriptionCommand("Pro", "Monthly"), CancellationToken.None);

        Assert.False(dto.DevMode);
        Assert.False(dto.IsFree);
        Assert.Equal("order_new", dto.OrderId);
        Assert.Equal(999m, dto.Amount);
    }

    [Fact]
    public async Task Unconfigured_DevMode_NoOrder()
    {
        var (db, ctx) = NewDb();
        await SeedAsync(db, ctx.TenantId);

        var dto = await Handler(db, ctx, new FakeRazorpayService { Configured = false })
            .Handle(new InitiateSubscriptionCommand("Pro", "Monthly"), CancellationToken.None);

        Assert.True(dto.DevMode);
        Assert.Null(dto.OrderId);
    }

    /// <summary>
    /// Razorpay 400s an order whose receipt is over 40 characters. "sub-{tenantId:N}-{planName}" was 42
    /// for Basic and 47 for Enterprise, so upgrading to either tier always failed at the gateway.
    /// </summary>
    [Theory]
    [InlineData(PlanType.Basic)]
    [InlineData(PlanType.Pro)]
    [InlineData(PlanType.Enterprise)]
    public async Task PaidUpgrade_ReceiptStaysWithinRazorpayLimit(PlanType planType)
    {
        var (db, ctx) = NewDb();
        await SeedAsync(db, ctx.TenantId, planType: planType);
        var rp = new FakeRazorpayService { Configured = true };

        await Handler(db, ctx, rp).Handle(
            new InitiateSubscriptionCommand(planType.ToString(), "Monthly"), CancellationToken.None);

        Assert.NotNull(rp.LastReceipt);
        Assert.True(rp.LastReceipt!.Length <= 40, $"receipt '{rp.LastReceipt}' is {rp.LastReceipt.Length} chars");
    }

    [Fact]
    public async Task FullDiscount_IsFree_NoOrder()
    {
        var (db, ctx) = NewDb();
        await SeedAsync(db, ctx.TenantId, SubscriptionDiscountType.Percentage, 100m);

        var dto = await Handler(db, ctx, new FakeRazorpayService { Configured = true })
            .Handle(new InitiateSubscriptionCommand("Pro", "Monthly"), CancellationToken.None);

        Assert.True(dto.IsFree);
        Assert.Null(dto.OrderId);
        Assert.Equal(0m, dto.Amount);
    }
}
