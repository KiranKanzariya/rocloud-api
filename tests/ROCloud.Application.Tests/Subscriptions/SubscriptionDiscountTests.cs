using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Features.Platform.Tenants.Commands.GrantFreeMonths;
using ROCloud.Application.Features.Platform.Tenants.Commands.SetTenantSubscriptionDiscount;
using ROCloud.Application.Features.Subscription;
using ROCloud.Domain.Entities.Platform;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;

namespace ROCloud.Application.Tests.Subscriptions;

public class SubscriptionDiscountTests
{
    private static AppDbContext NewDb()
        => new(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"subdisc-{Guid.NewGuid()}").Options,
            new TenantContext { TenantId = Guid.NewGuid() });

    private static async Task<Tenant> SeedTenantAsync(AppDbContext db, decimal price = 999m)
    {
        var plan = new Plan { Id = Guid.NewGuid(), Name = "Pro", PlanType = PlanType.Pro, MonthlyPrice = price, YearlyPrice = price * 10 };
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(), PlanId = plan.Id, Name = "Sharma Water", Subdomain = "sharma",
            OwnerName = "Owner", OwnerEmail = "o@x.com", OwnerMobile = "9", Status = TenantStatus.Trial,
            TrialEndsAt = DateTime.UtcNow.AddDays(5)
        };
        db.Plans.Add(plan);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    // ── calculator ──────────────────────────────────────────────────────────
    [Theory]
    [InlineData("Percentage", 100, 999, 0)]      // fully free
    [InlineData("Percentage", 50, 999, 499.50)]  // half off
    [InlineData("Fixed", 200, 999, 799)]         // ₹200 off
    [InlineData("Fixed", 5000, 999, 0)]          // capped at price (never negative)
    [InlineData("None", 0, 999, 999)]            // full price
    public void Net_ComputesExpectedPrice(string type, decimal value, decimal price, decimal expected)
    {
        var t = Enum.Parse<SubscriptionDiscountType>(type);
        Assert.Equal(expected, SubscriptionDiscountCalculator.Net(t, value, price));
    }

    /// <summary>
    /// The 2026-08 India repricing raised Enterprise from ₹5,999 to ₹7,999 (₹59,990 → ₹79,990
    /// yearly). Existing Enterprise tenants keep their old rate through a standing 25% discount
    /// rather than a second plan row.
    ///
    /// It has to be Percentage, not Fixed: the discount applies to whichever gross is being billed,
    /// so a Fixed ₹2,000 would be right monthly and leave a yearly tenant paying ₹77,990 instead of
    /// ₹59,990. The old and new prices happen to sit at exactly 0.75 on BOTH cycles, so one
    /// percentage grandfathers monthly and yearly tenants alike — that is why this number works, and
    /// why it must be rechecked if either price moves again.
    /// </summary>
    [Theory]
    [InlineData(7999, 5999.25)]     // monthly: was ₹5,999
    [InlineData(79990, 59992.50)]   // yearly:  was ₹59,990
    public void LegacyEnterpriseRate_IsHeldByAPercentageOnBothCycles(decimal gross, decimal expectedNet)
    {
        var net = SubscriptionDiscountCalculator.Net(SubscriptionDiscountType.Percentage, 25m, gross);

        Assert.Equal(expectedNet, net);
    }

    [Fact]
    public void AFixedDiscountWouldNotGrandfatherAYearlyTenant()
    {
        // Documents the trap rather than the fix: ₹2,000 off the yearly gross leaves them ₹18,000
        // worse off than the ₹59,990 they used to pay.
        var net = SubscriptionDiscountCalculator.Net(SubscriptionDiscountType.Fixed, 2000m, 79990m);

        Assert.Equal(77990m, net);
        Assert.NotEqual(59990m, net);
    }

    // ── set discount command ────────────────────────────────────────────────
    [Fact]
    public async Task SetDiscount_PersistsTypeAndValue()
    {
        var db = NewDb();
        var tenant = await SeedTenantAsync(db);

        await new SetTenantSubscriptionDiscountCommandHandler(db)
            .Handle(new SetTenantSubscriptionDiscountCommand(tenant.Id, "Percentage", 25m), CancellationToken.None);

        var fresh = await db.Tenants.FirstAsync(t => t.Id == tenant.Id);
        Assert.Equal(SubscriptionDiscountType.Percentage, fresh.SubscriptionDiscountType);
        Assert.Equal(25m, fresh.SubscriptionDiscountValue);
    }

    [Fact]
    public async Task SetDiscount_None_ClearsValue()
    {
        var db = NewDb();
        var tenant = await SeedTenantAsync(db);
        tenant.SubscriptionDiscountType = SubscriptionDiscountType.Fixed;
        tenant.SubscriptionDiscountValue = 300m;
        await db.SaveChangesAsync();

        await new SetTenantSubscriptionDiscountCommandHandler(db)
            .Handle(new SetTenantSubscriptionDiscountCommand(tenant.Id, "None", 300m), CancellationToken.None);

        var fresh = await db.Tenants.FirstAsync(t => t.Id == tenant.Id);
        Assert.Equal(SubscriptionDiscountType.None, fresh.SubscriptionDiscountType);
        Assert.Equal(0m, fresh.SubscriptionDiscountValue);
    }

    // ── grant free months ───────────────────────────────────────────────────
    [Fact]
    public async Task GrantFreeMonths_ExtendsFromTrialEnd_AndActivates()
    {
        var db = NewDb();
        var tenant = await SeedTenantAsync(db);
        var trialEnd = tenant.TrialEndsAt!.Value;

        var newEnd = await new GrantFreeMonthsCommandHandler(db)
            .Handle(new GrantFreeMonthsCommand(tenant.Id, 3), CancellationToken.None);

        var fresh = await db.Tenants.FirstAsync(t => t.Id == tenant.Id);
        Assert.Equal(TenantStatus.Active, fresh.Status);
        Assert.Null(fresh.TrialEndsAt);
        // Extended from the trial end (the latest basis), not shortened to now+3.
        Assert.Equal(trialEnd.AddMonths(3).Date, newEnd.Date);
    }
}
