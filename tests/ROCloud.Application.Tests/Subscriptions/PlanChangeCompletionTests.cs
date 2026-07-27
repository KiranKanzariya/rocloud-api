using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Features.Subscription.Commands.CompleteUpgrade;
using ROCloud.Application.Features.Subscription.Services;
using ROCloud.Domain.Entities.Platform;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;

namespace ROCloud.Application.Tests.Subscriptions;

/// <summary>
/// End-to-end behaviour of a mid-cycle plan change, on top of <see cref="PlanChangeCalculatorTests"/>
/// (which covers the arithmetic). The rule under test: <b>a plan change alters what you get, not how
/// long you have.</b>
///
/// The bug this replaced: every plan change ran through the renewal path, so switching tier charged a
/// full cycle AND pushed the renewal date out a month. Three changes in one afternoon bought three
/// months the owner never asked for, and the middle invoice billed the old plan's price for a period
/// the tenant spent on the new one.
/// </summary>
public class PlanChangeCompletionTests
{
    private static (AppDbContext Db, TenantContext Ctx) NewDb()
    {
        var ctx = new TenantContext { TenantId = Guid.NewGuid() };
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"pc-{Guid.NewGuid()}").Options, ctx);
        return (db, ctx);
    }

    private static async Task<(Plan Basic, Plan Pro)> SeedAsync(
        AppDbContext db, Guid tenantId, PlanType current, DateTime? endsAt)
    {
        var basic = new Plan
        {
            Id = Guid.NewGuid(), Name = "Basic", PlanType = PlanType.Basic,
            MonthlyPrice = 1099m, YearlyPrice = 10990m, IsActive = true,
            MaxCustomers = Plan.Unlimited, MaxUsers = Plan.Unlimited, MaxDeliveryBoys = Plan.Unlimited,
        };
        var pro = new Plan
        {
            Id = Guid.NewGuid(), Name = "Pro", PlanType = PlanType.Pro,
            MonthlyPrice = 2499m, YearlyPrice = 24990m, IsActive = true,
            MaxCustomers = Plan.Unlimited, MaxUsers = Plan.Unlimited, MaxDeliveryBoys = Plan.Unlimited,
        };
        db.Plans.AddRange(basic, pro);
        db.Tenants.Add(new Tenant
        {
            Id = tenantId, PlanId = current == PlanType.Basic ? basic.Id : pro.Id,
            Name = "Co", Subdomain = "co", OwnerName = "O", OwnerEmail = "o@x.com", OwnerMobile = "9",
            Status = TenantStatus.Active, SubscriptionEndsAt = endsAt,
        });
        await db.SaveChangesAsync();
        return (basic, pro);
    }

    private static CompleteUpgradeCommandHandler Handler(AppDbContext db, TenantContext ctx)
        => new(db, ctx, new FakeRazorpayService(), new NoOpSubscriptionInvoiceDelivery(), new Auth.FakeAppSettings());

    [Fact]
    public async Task Upgrade_DoesNotMoveTheRenewalDate()
    {
        var (db, ctx) = NewDb();
        var endsAt = DateTime.UtcNow.AddDays(12);
        await SeedAsync(db, ctx.TenantId, PlanType.Basic, endsAt);

        await Handler(db, ctx).Handle(new CompleteUpgradeCommand("Pro", "Monthly"), CancellationToken.None);

        var tenant = await db.Tenants.FirstAsync(t => t.Id == ctx.TenantId);
        // The whole point: they bought a better plan for the days they already had, not extra days.
        Assert.Equal(endsAt, tenant.SubscriptionEndsAt);
    }

    [Fact]
    public async Task Upgrade_ChargesOnlyTheProratedDifference()
    {
        var (db, ctx) = NewDb();
        await SeedAsync(db, ctx.TenantId, PlanType.Basic, DateTime.UtcNow.AddDays(12));

        await Handler(db, ctx).Handle(new CompleteUpgradeCommand("Pro", "Monthly"), CancellationToken.None);

        var invoice = await db.SubscriptionInvoices.SingleAsync();
        // ~(2499 − 1099) × 12/31. Not ₹2,499 — that was the leak.
        Assert.InRange(invoice.Amount, 500m, 600m);
        Assert.Equal(invoice.GrossAmount - invoice.DiscountAmount, invoice.Amount);

        var ledger = await db.PlatformBillingTransactions.SingleAsync();
        Assert.Equal(invoice.Amount, ledger.Amount);
    }

    [Fact]
    public async Task Upgrade_AppliesThePlanImmediately()
    {
        var (db, ctx) = NewDb();
        var (_, pro) = await SeedAsync(db, ctx.TenantId, PlanType.Basic, DateTime.UtcNow.AddDays(12));

        await Handler(db, ctx).Handle(new CompleteUpgradeCommand("Pro", "Monthly"), CancellationToken.None);

        var tenant = await db.Tenants.FirstAsync(t => t.Id == ctx.TenantId);
        Assert.Equal(pro.Id, tenant.PlanId);
        Assert.Null(tenant.ScheduledPlanId);
    }

    [Fact]
    public async Task Downgrade_ChargesNothing_AndKeepsTheCurrentPlanUntilPeriodEnd()
    {
        var (db, ctx) = NewDb();
        var endsAt = DateTime.UtcNow.AddDays(12);
        var (basic, pro) = await SeedAsync(db, ctx.TenantId, PlanType.Pro, endsAt);

        await Handler(db, ctx).Handle(new CompleteUpgradeCommand("Basic", "Monthly"), CancellationToken.None);

        var tenant = await db.Tenants.FirstAsync(t => t.Id == ctx.TenantId);
        Assert.Equal(pro.Id, tenant.PlanId);            // still Pro — they paid for it
        Assert.Equal(basic.Id, tenant.ScheduledPlanId);  // Basic waits for period end
        Assert.Equal(endsAt, tenant.SubscriptionEndsAt);
        Assert.Empty(db.SubscriptionInvoices);           // nothing charged, nothing refunded
        Assert.Empty(db.PlatformBillingTransactions);
    }

    [Fact]
    public async Task ReselectingTheCurrentPlan_CancelsAPendingDowngrade_WithoutCharging()
    {
        var (db, ctx) = NewDb();
        var (basic, pro) = await SeedAsync(db, ctx.TenantId, PlanType.Pro, DateTime.UtcNow.AddDays(12));
        var handler = Handler(db, ctx);

        await handler.Handle(new CompleteUpgradeCommand("Basic", "Monthly"), CancellationToken.None);
        await handler.Handle(new CompleteUpgradeCommand("Pro", "Monthly"), CancellationToken.None);

        var tenant = await db.Tenants.FirstAsync(t => t.Id == ctx.TenantId);
        Assert.Null(tenant.ScheduledPlanId);   // undone — the only way back once one is scheduled
        Assert.Equal(pro.Id, tenant.PlanId);
        Assert.Empty(db.SubscriptionInvoices); // and it must not be billed as a renewal
    }

    [Fact]
    public async Task RepeatedUpgrades_DoNotStackExtraMonths()
    {
        // The reported bug, as a test: changing tier twice in one session used to buy two extra months.
        var (db, ctx) = NewDb();
        var endsAt = DateTime.UtcNow.AddDays(12);
        await SeedAsync(db, ctx.TenantId, PlanType.Basic, endsAt);
        var handler = Handler(db, ctx);

        await handler.Handle(new CompleteUpgradeCommand("Pro", "Monthly"), CancellationToken.None);
        await handler.Handle(new CompleteUpgradeCommand("Basic", "Monthly"), CancellationToken.None);
        await handler.Handle(new CompleteUpgradeCommand("Pro", "Monthly"), CancellationToken.None);

        var tenant = await db.Tenants.FirstAsync(t => t.Id == ctx.TenantId);
        Assert.Equal(endsAt, tenant.SubscriptionEndsAt);
    }

    [Fact]
    public async Task LapsedTenant_StillBuysAFullCycle()
    {
        // No live term to prorate against — this is a renewal, and must behave exactly as before.
        var (db, ctx) = NewDb();
        await SeedAsync(db, ctx.TenantId, PlanType.Basic, DateTime.UtcNow.AddDays(-3));

        await Handler(db, ctx).Handle(new CompleteUpgradeCommand("Pro", "Monthly"), CancellationToken.None);

        var tenant = await db.Tenants.FirstAsync(t => t.Id == ctx.TenantId);
        Assert.True(tenant.SubscriptionEndsAt > DateTime.UtcNow.AddDays(20));

        var invoice = await db.SubscriptionInvoices.SingleAsync();
        Assert.Equal(2499m, invoice.Amount);   // full cycle, not a prorated slice
    }

    [Fact]
    public async Task Upgrade_VoidsAStaleRenewalInvoicePricedAtTheOldPlan()
    {
        var (db, ctx) = NewDb();
        var (basic, _) = await SeedAsync(db, ctx.TenantId, PlanType.Basic, DateTime.UtcNow.AddDays(3));
        var tenant = await db.Tenants.FirstAsync(t => t.Id == ctx.TenantId);
        var stale = await SubscriptionInvoiceFactory.BuildAsync(
            db, tenant, basic, "Monthly", DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)),
            SubscriptionInvoiceStatus.Pending, "Basic plan — 1 month renewal", CancellationToken.None);
        db.SubscriptionInvoices.Add(stale);
        await db.SaveChangesAsync();

        await Handler(db, ctx).Handle(new CompleteUpgradeCommand("Pro", "Monthly"), CancellationToken.None);

        var voided = await db.SubscriptionInvoices.FirstAsync(i => i.Id == stale.Id);
        Assert.Equal(SubscriptionInvoiceStatus.Void, voided.Status);
    }
}
