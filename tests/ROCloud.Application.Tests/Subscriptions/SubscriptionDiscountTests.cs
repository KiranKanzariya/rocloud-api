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
