using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Features.Platform.Tenants.Commands.SetTenantStatus;
using ROCloud.Domain.Entities.Platform;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;

namespace ROCloud.Application.Tests.Subscriptions;

/// <summary>
/// Platform admin suspend / reactivate. The important case is a TRIAL tenant: reactivating it must
/// restore the trial, NOT hand out a free paid month (which is what a blanket "+1 month on reactivate"
/// used to do — a tenant suspended mid-trial came back as Active with a month of unpaid access).
/// </summary>
public class SetTenantStatusTests
{
    private static AppDbContext NewDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"sts-{Guid.NewGuid()}").Options,
            new TenantContext());

    private static async Task<Guid> SeedTenantAsync(
        AppDbContext db, TenantStatus status, DateTime? trialEndsAt, DateTime? subscriptionEndsAt,
        DateTime? suspendedAt = null)
    {
        var id = Guid.NewGuid();
        var plan = new Plan { Id = Guid.NewGuid(), Name = "Basic", PlanType = PlanType.Basic };
        db.Plans.Add(plan);
        db.Tenants.Add(new Tenant
        {
            Id = id, PlanId = plan.Id, Name = "Co", Subdomain = "acme",
            OwnerName = "O", OwnerEmail = "o@x.com", OwnerMobile = "9",
            Status = status, TrialEndsAt = trialEndsAt, SubscriptionEndsAt = subscriptionEndsAt,
            SuspendedAt = suspendedAt,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static Task RunAsync(AppDbContext db, Guid id, string status, bool credit = false)
        => new SetTenantStatusCommandHandler(db)
            .Handle(new SetTenantStatusCommand(id, status, credit), CancellationToken.None);

    [Fact]
    public async Task Reactivating_ASuspendedTrial_RestoresTrial_AndGrantsNoPaidMonth()
    {
        var db = NewDb();
        var trialEnds = DateTime.UtcNow.AddDays(3);
        var id = await SeedTenantAsync(db, TenantStatus.Suspended, trialEnds, subscriptionEndsAt: null);

        await RunAsync(db, id, "Active");

        var t = await db.Tenants.FirstAsync(x => x.Id == id);
        Assert.Equal(TenantStatus.Trial, t.Status);          // back on trial, not "Active"
        Assert.Null(t.SubscriptionEndsAt);                   // no free month gifted
        Assert.Equal(trialEnds, t.TrialEndsAt);              // trial window untouched
    }

    [Fact]
    public async Task Reactivating_AnExpiredTrial_RestoresTrial_SoTheNightlyJobResumesDunning()
    {
        var db = NewDb();
        var trialEnds = DateTime.UtcNow.AddDays(-5);         // already lapsed while suspended
        var id = await SeedTenantAsync(db, TenantStatus.Suspended, trialEnds, subscriptionEndsAt: null);

        await RunAsync(db, id, "Active");

        var t = await db.Tenants.FirstAsync(x => x.Id == id);
        Assert.Equal(TenantStatus.Trial, t.Status);
        Assert.Null(t.SubscriptionEndsAt);
        Assert.Equal(trialEnds, t.TrialEndsAt);
    }

    [Fact]
    public async Task Reactivating_ALapsedPaidTenant_ComesBackOverdue_WithNoFreeMonth()
    {
        var db = NewDb();
        var ends = DateTime.UtcNow.AddDays(-10);
        var id = await SeedTenantAsync(db, TenantStatus.Suspended, trialEndsAt: null, subscriptionEndsAt: ends);

        await RunAsync(db, id, "Active");

        var t = await db.Tenants.FirstAsync(x => x.Id == id);
        Assert.Equal(TenantStatus.Overdue, t.Status);   // dunning resumes; they still owe money
        Assert.Equal(ends, t.SubscriptionEndsAt);       // NOT pushed forward — no gifted month
    }

    [Fact]
    public async Task Suspending_StampsSuspendedAt()
    {
        var db = NewDb();
        var id = await SeedTenantAsync(
            db, TenantStatus.Active, trialEndsAt: null, subscriptionEndsAt: DateTime.UtcNow.AddDays(20));

        await RunAsync(db, id, "Suspended");

        var t = await db.Tenants.FirstAsync(x => x.Id == id);
        Assert.NotNull(t.SuspendedAt);
    }

    [Fact]
    public async Task Reactivating_WithCredit_GivesBackTheDaysAPayingTenantLost()
    {
        var db = NewDb();
        var ends = DateTime.UtcNow.AddDays(20);
        var suspendedAt = DateTime.UtcNow.AddDays(-6);      // blocked for ~6 days of paid time
        var id = await SeedTenantAsync(db, TenantStatus.Suspended, null, ends, suspendedAt);

        await RunAsync(db, id, "Active", credit: true);

        var t = await db.Tenants.FirstAsync(x => x.Id == id);
        Assert.Equal(TenantStatus.Active, t.Status);
        // ~6 days handed back (allow a small window for execution time).
        var credited = (t.SubscriptionEndsAt!.Value - ends).TotalDays;
        Assert.InRange(credited, 5.9, 6.1);
        Assert.Null(t.SuspendedAt);                          // cleared on reactivate
    }

    [Fact]
    public async Task Reactivating_WithoutCredit_LeavesTheEndDateAlone()
    {
        var db = NewDb();
        var ends = DateTime.UtcNow.AddDays(20);
        var id = await SeedTenantAsync(db, TenantStatus.Suspended, null, ends, DateTime.UtcNow.AddDays(-6));

        await RunAsync(db, id, "Active");                    // credit NOT requested

        var t = await db.Tenants.FirstAsync(x => x.Id == id);
        Assert.Equal(TenantStatus.Active, t.Status);
        Assert.Equal(ends, t.SubscriptionEndsAt);
    }

    [Fact]
    public async Task Credit_EarnsNothing_WhenTheSubscriptionHadAlreadyExpiredBeforeSuspension()
    {
        var db = NewDb();
        // The non-payment path: lapsed 30 days ago, suspended 10 days ago → no paid time was lost.
        var ends = DateTime.UtcNow.AddDays(-30);
        var id = await SeedTenantAsync(db, TenantStatus.Suspended, null, ends, DateTime.UtcNow.AddDays(-10));

        await RunAsync(db, id, "Active", credit: true);

        var t = await db.Tenants.FirstAsync(x => x.Id == id);
        Assert.Equal(TenantStatus.Overdue, t.Status);
        Assert.Equal(ends, t.SubscriptionEndsAt);            // nothing credited
    }

    [Fact]
    public async Task Reactivating_ACurrentPaidTenant_LeavesTheEndDateAlone()
    {
        var db = NewDb();
        var ends = DateTime.UtcNow.AddDays(20);
        var id = await SeedTenantAsync(db, TenantStatus.Suspended, trialEndsAt: null, subscriptionEndsAt: ends);

        await RunAsync(db, id, "Active");

        var t = await db.Tenants.FirstAsync(x => x.Id == id);
        Assert.Equal(TenantStatus.Active, t.Status);
        Assert.Equal(ends, t.SubscriptionEndsAt);
    }

    [Fact]
    public async Task Suspending_ATrial_KeepsTheTrialDate_SoItCanBeRestored()
    {
        var db = NewDb();
        var trialEnds = DateTime.UtcNow.AddDays(3);
        var id = await SeedTenantAsync(db, TenantStatus.Trial, trialEnds, subscriptionEndsAt: null);

        await RunAsync(db, id, "Suspended");

        var t = await db.Tenants.FirstAsync(x => x.Id == id);
        Assert.Equal(TenantStatus.Suspended, t.Status);
        Assert.Equal(trialEnds, t.TrialEndsAt);
        Assert.Null(t.SubscriptionEndsAt);
    }
}
