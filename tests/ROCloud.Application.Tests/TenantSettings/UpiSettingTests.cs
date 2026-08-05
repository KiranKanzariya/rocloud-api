using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Features.TenantSettings.Commands.UpdateTenantSettings;
using ROCloud.Domain.Entities.Platform;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Tests.TenantSettings;

/// <summary>
/// The scan-to-pay QR is opt-in and cannot be switched on without an id to pay into — the same shape
/// as the GST rule next door. An invoice carrying a QR that resolves to nothing is worse than no QR:
/// the customer scans, nothing happens, and they stop trusting the document.
/// </summary>
public class UpiSettingTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static (AppDbContext Db, TenantContext Ctx) NewDb()
    {
        var ctx = new TenantContext { TenantId = TenantId };
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"upi-{Guid.NewGuid()}").Options, ctx);
        return (db, ctx);
    }

    private static async Task SeedTenantAsync(AppDbContext db, string? vpa = null, bool qrEnabled = false)
    {
        db.Tenants.Add(new Tenant
        {
            Id = TenantId, Name = "Dabhi RO Water", Subdomain = "dabhi",
            OwnerName = "O", OwnerEmail = "o@x.com", OwnerMobile = "9",
            Status = TenantStatus.Active, DefaultLanguage = "en",
            UpiVpa = vpa, UpiQrEnabled = qrEnabled,
        });
        await db.SaveChangesAsync();
    }

    private static UpdateTenantSettingsCommand Settings(string? vpa, bool qrEnabled) =>
        new("Dabhi RO Water", null, false, 18m, null, null, null, null, null, null, "en",
            UpiVpa: vpa, UpiPayeeName: null, UpiQrEnabled: qrEnabled);

    [Fact]
    public void ANewTenantHasTheQrOffByDefault()
        => Assert.False(new Tenant().UpiQrEnabled);

    [Fact]
    public async Task EnablingTheQr_WithoutAUpiId_IsRejected()
    {
        var (db, ctx) = NewDb();
        await SeedTenantAsync(db);

        await Assert.ThrowsAsync<ValidationException>(() =>
            new UpdateTenantSettingsCommandHandler(db, ctx).Handle(Settings(null, true), CancellationToken.None));

        Assert.False((await db.Tenants.FirstAsync()).UpiQrEnabled);   // unchanged
    }

    [Fact]
    public async Task AUpiIdCanBeSaved_WithoutTurningTheQrOn()
    {
        // Saving the id and switching the QR on are separate decisions — an owner can record their
        // UPI id without putting it in front of customers yet.
        var (db, ctx) = NewDb();
        await SeedTenantAsync(db);

        await new UpdateTenantSettingsCommandHandler(db, ctx)
            .Handle(Settings("dabhiro@okaxis", false), CancellationToken.None);

        var t = await db.Tenants.FirstAsync();
        Assert.Equal("dabhiro@okaxis", t.UpiVpa);
        Assert.False(t.UpiQrEnabled);
    }

    [Fact]
    public async Task ClearingTheUpiId_AlsoSwitchesTheQrOff()
    {
        // Otherwise the flag stays on with nothing behind it and every invoice silently loses its QR
        // while the settings screen still shows it enabled.
        var (db, ctx) = NewDb();
        await SeedTenantAsync(db, vpa: "dabhiro@okaxis", qrEnabled: true);

        await new UpdateTenantSettingsCommandHandler(db, ctx)
            .Handle(Settings(null, false), CancellationToken.None);

        var t = await db.Tenants.FirstAsync();
        Assert.Null(t.UpiVpa);
        Assert.False(t.UpiQrEnabled);
    }

    [Fact]
    public async Task TheUpiIdIsTrimmed_SoAPastedIdDoesNotBreakTheQr()
    {
        var (db, ctx) = NewDb();
        await SeedTenantAsync(db);

        await new UpdateTenantSettingsCommandHandler(db, ctx)
            .Handle(Settings("  dabhiro@okaxis  ", false), CancellationToken.None);

        Assert.Equal("dabhiro@okaxis", (await db.Tenants.FirstAsync()).UpiVpa);
    }

    [Theory]
    [InlineData("dabhiro@okaxis", true)]
    [InlineData("dabhi.ro-1_x@ybl", true)]
    [InlineData("dabhiro@", false)]
    [InlineData("@okaxis", false)]
    [InlineData("dabhi ro@okaxis", false)]
    [InlineData("dabhiro", false)]
    public void VpaShapeIsValidated(string vpa, bool valid)
    {
        var result = new UpdateTenantSettingsCommandValidator().Validate(Settings(vpa, false));
        Assert.Equal(valid, result.IsValid);
    }
}
