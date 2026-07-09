using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Features.Platform.Tenants.Commands.ChangeTenantSubdomain;
using ROCloud.Domain.Entities.Platform;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Tests.Subscriptions;

/// <summary>#4: platform admin can rename a tenant's (otherwise permanent) subdomain.</summary>
public class ChangeTenantSubdomainTests
{
    private static AppDbContext NewDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"sub-{Guid.NewGuid()}").Options,
            new TenantContext());

    private static async Task<Guid> SeedTenantAsync(AppDbContext db, string subdomain, string email = "o@x.com")
    {
        var id = Guid.NewGuid();
        var plan = new Plan { Id = Guid.NewGuid(), Name = "Basic", PlanType = PlanType.Basic };
        db.Plans.Add(plan);
        db.Tenants.Add(new Tenant
        {
            Id = id, PlanId = plan.Id, Name = "Co", Subdomain = subdomain,
            OwnerName = "O", OwnerEmail = email, OwnerMobile = "9", Status = TenantStatus.Active
        });
        await db.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task Rename_ToNewSlug_NormalisesAndUpdates()
    {
        var db = NewDb();
        var id = await SeedTenantAsync(db, "acmee");

        var result = await new ChangeTenantSubdomainCommandHandler(db)
            .Handle(new ChangeTenantSubdomainCommand(id, "Acme Water"), CancellationToken.None);

        Assert.Equal("acme-water", result);
        Assert.Equal("acme-water", (await db.Tenants.FirstAsync(t => t.Id == id)).Subdomain);
    }

    [Fact]
    public async Task Rename_ToReservedLabel_Throws()
    {
        var db = NewDb();
        var id = await SeedTenantAsync(db, "acme");

        await Assert.ThrowsAsync<ValidationException>(() => new ChangeTenantSubdomainCommandHandler(db)
            .Handle(new ChangeTenantSubdomainCommand(id, "admin"), CancellationToken.None));
    }

    [Fact]
    public async Task Rename_ToTakenSubdomain_Throws()
    {
        var db = NewDb();
        var id = await SeedTenantAsync(db, "acme");
        await SeedTenantAsync(db, "blue", "b@x.com");

        await Assert.ThrowsAsync<ValidationException>(() => new ChangeTenantSubdomainCommandHandler(db)
            .Handle(new ChangeTenantSubdomainCommand(id, "blue"), CancellationToken.None));
    }

    [Fact]
    public async Task Rename_TooShort_Throws()
    {
        var db = NewDb();
        var id = await SeedTenantAsync(db, "acme");

        await Assert.ThrowsAsync<ValidationException>(() => new ChangeTenantSubdomainCommandHandler(db)
            .Handle(new ChangeTenantSubdomainCommand(id, "ab"), CancellationToken.None));
    }
}
