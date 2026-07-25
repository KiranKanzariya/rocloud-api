using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Features.TenantSettings.Commands.UpdateTenantSettings;
using ROCloud.Domain.Entities.Platform;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Tests.TenantSettings;

/// <summary>
/// GST defaults OFF (most small water suppliers are not registered), and it cannot be TURNED ON without
/// a GSTIN — otherwise the tenant issues a "tax invoice" with no registration number, an improper
/// document. A tenant already in that legacy state is not blocked from saving unrelated fields.
/// </summary>
public class GstSettingTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static (AppDbContext Db, TenantContext Ctx) NewDb()
    {
        var ctx = new TenantContext { TenantId = TenantId };
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"gst-{Guid.NewGuid()}").Options, ctx);
        return (db, ctx);
    }

    private static async Task SeedTenantAsync(AppDbContext db, bool gstEnabled, string? gstNumber)
    {
        db.Tenants.Add(new Tenant
        {
            Id = TenantId, Name = "Co", Subdomain = "co",
            OwnerName = "O", OwnerEmail = "o@x.com", OwnerMobile = "9",
            Status = TenantStatus.Active, DefaultLanguage = "en",
            GstEnabled = gstEnabled, GstNumber = gstNumber,
        });
        await db.SaveChangesAsync();
    }

    private static UpdateTenantSettingsCommand Settings(bool gstEnabled, string? gstNumber) =>
        new("Co", gstNumber, gstEnabled, 18m, null, null, null, null, null, null, "en");

    [Fact]
    public void ANewTenantHasGstOffByDefault()
    {
        // The default lives on the entity (and mirrored as the DB column default); a freshly constructed
        // tenant must not silently be on the tax-invoice path.
        Assert.False(new Tenant().GstEnabled);
    }

    [Fact]
    public async Task EnablingGst_WithoutAGstin_IsRejected()
    {
        var (db, ctx) = NewDb();
        await SeedTenantAsync(db, gstEnabled: false, gstNumber: null);

        await Assert.ThrowsAsync<ValidationException>(() =>
            new UpdateTenantSettingsCommandHandler(db, ctx).Handle(Settings(true, null), CancellationToken.None));

        Assert.False((await db.Tenants.FirstAsync()).GstEnabled);   // unchanged
    }

    [Fact]
    public async Task EnablingGst_WithAGstin_IsAllowed()
    {
        var (db, ctx) = NewDb();
        await SeedTenantAsync(db, gstEnabled: false, gstNumber: null);

        await new UpdateTenantSettingsCommandHandler(db, ctx)
            .Handle(Settings(true, "24AAAAA0000A1Z5"), CancellationToken.None);

        var t = await db.Tenants.FirstAsync();
        Assert.True(t.GstEnabled);
        Assert.Equal("24AAAAA0000A1Z5", t.GstNumber);
    }

    [Fact]
    public async Task ALegacyTenantOnWithoutAGstin_CanStillSaveOtherFields()
    {
        // Predates the rule: gst already on, no GSTIN. It must not be blocked from an unrelated edit —
        // the guard fires only on the OFF→ON transition, not on every save.
        var (db, ctx) = NewDb();
        await SeedTenantAsync(db, gstEnabled: true, gstNumber: null);

        await new UpdateTenantSettingsCommandHandler(db, ctx)
            .Handle(Settings(true, null) with { City = "Rajkot" }, CancellationToken.None);

        var t = await db.Tenants.FirstAsync();
        Assert.Equal("Rajkot", t.City);
        Assert.True(t.GstEnabled);   // left as it was
    }

    [Fact]
    public async Task TurningGstOff_NeverNeedsAGstin()
    {
        var (db, ctx) = NewDb();
        await SeedTenantAsync(db, gstEnabled: true, gstNumber: null);

        await new UpdateTenantSettingsCommandHandler(db, ctx)
            .Handle(Settings(false, null), CancellationToken.None);

        Assert.False((await db.Tenants.FirstAsync()).GstEnabled);
    }
}
