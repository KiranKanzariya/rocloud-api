using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Features.Platform.Tenants.Commands.ChangeTenantPlan;
using ROCloud.Application.Features.Subscription.Commands.CompleteUpgrade;
using ROCloud.Domain.Entities.Platform;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Tests.Subscriptions;

/// <summary>#7: a plan change must never drop a tenant below its current usage — team members,
/// delivery boys or customers.</summary>
public class PlanChangeGuardTests
{
    private static (AppDbContext Db, TenantContext Ctx, Guid TenantId) NewDb()
    {
        var ctx = new TenantContext { TenantId = Guid.NewGuid() };
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"pcg-{Guid.NewGuid()}").Options, ctx);
        return (db, ctx, ctx.TenantId);
    }

    private static async Task SeedAsync(AppDbContext db, Guid tenantId, int currentMaxUsers, int userCount)
    {
        var cur = new Plan { Id = Guid.NewGuid(), Name = "Pro", PlanType = PlanType.Pro, MaxUsers = currentMaxUsers, IsActive = true };
        db.Plans.Add(cur);
        db.Tenants.Add(new Tenant
        {
            Id = tenantId, PlanId = cur.Id, Name = "Co", Subdomain = "co",
            OwnerName = "O", OwnerEmail = "o@x.com", OwnerMobile = "9", Status = TenantStatus.Active
        });
        for (var i = 0; i < userCount; i++)
            db.Users.Add(new User { Id = Guid.NewGuid(), TenantId = tenantId, RoleId = Guid.NewGuid(), Name = $"U{i}", Email = $"u{i}@x.com", IsActive = true });
        db.Plans.Add(new Plan { Id = Guid.NewGuid(), Name = "Basic", PlanType = PlanType.Basic, MaxUsers = 3, IsActive = true });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ChangePlan_DowngradeBelowUsage_Throws()
    {
        var (db, _, tenantId) = NewDb();
        await SeedAsync(db, tenantId, currentMaxUsers: 10, userCount: 4);   // 4 users, Basic allows 3

        await Assert.ThrowsAsync<ValidationException>(() => new ChangeTenantPlanCommandHandler(db)
            .Handle(new ChangeTenantPlanCommand(tenantId, "Basic"), CancellationToken.None));
    }

    [Fact]
    public async Task ChangePlan_DowngradeThatFits_Succeeds()
    {
        var (db, _, tenantId) = NewDb();
        await SeedAsync(db, tenantId, currentMaxUsers: 10, userCount: 3);   // exactly at the Basic cap

        await new ChangeTenantPlanCommandHandler(db)
            .Handle(new ChangeTenantPlanCommand(tenantId, "Basic"), CancellationToken.None);

        var basic = await db.Plans.FirstAsync(p => p.PlanType == PlanType.Basic);
        Assert.Equal(basic.Id, (await db.Tenants.FirstAsync(t => t.Id == tenantId)).PlanId);
    }

    [Fact]
    public async Task CompleteUpgrade_DowngradeBelowUsage_Throws()
    {
        var (db, ctx, tenantId) = NewDb();
        await SeedAsync(db, tenantId, currentMaxUsers: 10, userCount: 4);

        await Assert.ThrowsAsync<ValidationException>(() => new CompleteUpgradeCommandHandler(db, ctx, new FakeRazorpayService(), new NoOpSubscriptionInvoiceDelivery(), new Auth.FakeAppSettings())
            .Handle(new CompleteUpgradeCommand("Basic", "Monthly"), CancellationToken.None));
    }

    [Fact]
    public async Task ChangePlan_DowngradeOverCustomerCap_Throws()
    {
        var (db, _, tenantId) = NewDb();
        var pro = new Plan { Id = Guid.NewGuid(), Name = "Pro", PlanType = PlanType.Pro, MaxUsers = 0, MaxCustomers = 0, MaxDeliveryBoys = 0, IsActive = true };
        var basic = new Plan { Id = Guid.NewGuid(), Name = "Basic", PlanType = PlanType.Basic, MaxUsers = 3, MaxCustomers = 2, MaxDeliveryBoys = 1, IsActive = true };
        db.Plans.AddRange(pro, basic);
        db.Tenants.Add(new Tenant
        {
            Id = tenantId, PlanId = pro.Id, Name = "Co", Subdomain = "co",
            OwnerName = "O", OwnerEmail = "o@x.com", OwnerMobile = "9", Status = TenantStatus.Active
        });
        for (var i = 0; i < 3; i++)   // 3 customers, Basic allows 2
            db.Customers.Add(new Customer { Id = Guid.NewGuid(), TenantId = tenantId, Name = $"C{i}" });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() => new ChangeTenantPlanCommandHandler(db)
            .Handle(new ChangeTenantPlanCommand(tenantId, "Basic"), CancellationToken.None));
    }

    [Fact]
    public async Task ChangePlan_DowngradeOverDeliveryBoyCap_Throws()
    {
        var (db, _, tenantId) = NewDb();
        var pro = new Plan { Id = Guid.NewGuid(), Name = "Pro", PlanType = PlanType.Pro, MaxUsers = 0, MaxCustomers = 0, MaxDeliveryBoys = 0, IsActive = true };
        var basic = new Plan { Id = Guid.NewGuid(), Name = "Basic", PlanType = PlanType.Basic, MaxUsers = 3, MaxCustomers = 200, MaxDeliveryBoys = 1, IsActive = true };
        var role = new Role { Id = Guid.NewGuid(), TenantId = tenantId, Name = "DeliveryBoy", IsSystem = true };
        db.Plans.AddRange(pro, basic);
        db.Roles.Add(role);
        db.Tenants.Add(new Tenant
        {
            Id = tenantId, PlanId = pro.Id, Name = "Co", Subdomain = "co",
            OwnerName = "O", OwnerEmail = "o@x.com", OwnerMobile = "9", Status = TenantStatus.Active
        });
        for (var i = 0; i < 2; i++)   // 2 active delivery boys, Basic allows 1 (and 2 users is under the 3 user cap)
            db.Users.Add(new User { Id = Guid.NewGuid(), TenantId = tenantId, RoleId = role.Id, Role = role, Name = $"D{i}", Email = $"d{i}@x.com", IsActive = true });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() => new ChangeTenantPlanCommandHandler(db)
            .Handle(new ChangeTenantPlanCommand(tenantId, "Basic"), CancellationToken.None));
    }
}
