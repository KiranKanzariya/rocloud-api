using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Features.Subscription.Commands.CompleteUpgrade;
using ROCloud.Domain.Entities.Platform;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Tests.Subscriptions;

/// <summary>#8 renewal dating, and the revenue-leak fix: a paid upgrade needs a verified payment.</summary>
public class CompleteUpgradeTests
{
    private static (AppDbContext Db, TenantContext Ctx) NewDb()
    {
        var ctx = new TenantContext { TenantId = Guid.NewGuid() };
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"cu-{Guid.NewGuid()}").Options, ctx);
        return (db, ctx);
    }

    private static async Task SeedAsync(AppDbContext db, Guid tenantId, DateTime? subscriptionEndsAt)
    {
        var plan = new Plan { Id = Guid.NewGuid(), Name = "Pro", PlanType = PlanType.Pro, MonthlyPrice = 999m, YearlyPrice = 9990m, IsActive = true };
        db.Plans.Add(plan);
        db.Tenants.Add(new Tenant
        {
            Id = tenantId, PlanId = plan.Id, Name = "Co", Subdomain = "co",
            OwnerName = "O", OwnerEmail = "o@x.com", OwnerMobile = "9",
            Status = TenantStatus.Active, SubscriptionEndsAt = subscriptionEndsAt
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task RenewingEarly_ExtendsFromCurrentEndDate()
    {
        var (db, ctx) = NewDb();
        var endsAt = DateTime.UtcNow.AddDays(10);   // 10 days of paid time remaining
        await SeedAsync(db, ctx.TenantId, endsAt);

        await new CompleteUpgradeCommandHandler(db, ctx, new FakeRazorpayService(), new NoOpSubscriptionInvoiceDelivery(), new Auth.FakeAppSettings()).Handle(
            new CompleteUpgradeCommand("Pro", "Monthly"), CancellationToken.None);

        var tenant = await db.Tenants.FirstAsync(t => t.Id == ctx.TenantId);
        // Extended from now+10d, so the new end is well past now+30d (the old truncating behaviour).
        Assert.True(tenant.SubscriptionEndsAt > DateTime.UtcNow.AddDays(35));
    }

    [Fact]
    public async Task RenewingInsideGrace_BillsTheGraceDaysAlreadyUsed()
    {
        var (db, ctx) = NewDb();
        // Lapsed 5 days ago. Grace is 7, so the app has been fully working — and free — the whole time.
        await SeedAsync(db, ctx.TenantId, DateTime.UtcNow.AddDays(-5));

        await new CompleteUpgradeCommandHandler(db, ctx, new FakeRazorpayService(), new NoOpSubscriptionInvoiceDelivery(), new Auth.FakeAppSettings()).Handle(
            new CompleteUpgradeCommand("Pro", "Monthly"), CancellationToken.None);

        var tenant = await db.Tenants.FirstAsync(t => t.Id == ctx.TenantId);
        // The term runs from the OLD end date, so it ends ~25 days out, not ~30. Those 5 days were
        // used, so they are paid for — otherwise a habitually-late tenant collects them every cycle.
        Assert.True(tenant.SubscriptionEndsAt > DateTime.UtcNow.AddDays(23));
        Assert.True(tenant.SubscriptionEndsAt < DateTime.UtcNow.AddDays(27));
    }

    [Fact]
    public async Task RenewingAfterTheBlock_CreditsTheLockedOutDaysBack()
    {
        var (db, ctx) = NewDb();
        // Lapsed 20 days ago: 7 grace days used, then 13 days locked out.
        await SeedAsync(db, ctx.TenantId, DateTime.UtcNow.AddDays(-20));

        await new CompleteUpgradeCommandHandler(db, ctx, new FakeRazorpayService(), new NoOpSubscriptionInvoiceDelivery(), new Auth.FakeAppSettings()).Handle(
            new CompleteUpgradeCommand("Pro", "Monthly"), CancellationToken.None);

        var tenant = await db.Tenants.FirstAsync(t => t.Id == ctx.TenantId);
        // (now-20d) + 1 month + 13 credited days ≈ now+23d. Without the credit it would have expired
        // ~10 days from now — a month's price for ten days of service.
        Assert.True(tenant.SubscriptionEndsAt > DateTime.UtcNow.AddDays(21));
        Assert.True(tenant.SubscriptionEndsAt < DateTime.UtcNow.AddDays(25));
    }

    [Fact]
    public async Task PaidUpgrade_LiveKeys_NoOrder_Rejected()
    {
        var (db, ctx) = NewDb();
        await SeedAsync(db, ctx.TenantId, DateTime.UtcNow.AddDays(-1));
        var rp = new FakeRazorpayService { Configured = true };

        await Assert.ThrowsAsync<ValidationException>(() => new CompleteUpgradeCommandHandler(db, ctx, rp, new NoOpSubscriptionInvoiceDelivery(), new Auth.FakeAppSettings())
            .Handle(new CompleteUpgradeCommand("Pro", "Monthly"), CancellationToken.None));
    }

    [Fact]
    public async Task PaidUpgrade_LiveKeys_UnpaidOrder_Rejected()
    {
        var (db, ctx) = NewDb();
        await SeedAsync(db, ctx.TenantId, DateTime.UtcNow.AddDays(-1));
        var rp = new FakeRazorpayService { Configured = true };   // "order_x" not marked paid

        await Assert.ThrowsAsync<ValidationException>(() => new CompleteUpgradeCommandHandler(db, ctx, rp, new NoOpSubscriptionInvoiceDelivery(), new Auth.FakeAppSettings())
            .Handle(new CompleteUpgradeCommand("Pro", "Monthly", "order_x"), CancellationToken.None));
    }

    [Fact]
    public async Task PaidUpgrade_LiveKeys_VerifiedOrder_Completes()
    {
        var (db, ctx) = NewDb();
        await SeedAsync(db, ctx.TenantId, DateTime.UtcNow.AddDays(-1));
        var rp = new FakeRazorpayService { Configured = true };
        rp.PaidStatuses["order_ok"] = new(true, "pay_ok");

        await new CompleteUpgradeCommandHandler(db, ctx, rp, new NoOpSubscriptionInvoiceDelivery(), new Auth.FakeAppSettings())
            .Handle(new CompleteUpgradeCommand("Pro", "Monthly", "order_ok"), CancellationToken.None);

        Assert.Equal(TenantStatus.Active, (await db.Tenants.FirstAsync(t => t.Id == ctx.TenantId)).Status);
    }
}
