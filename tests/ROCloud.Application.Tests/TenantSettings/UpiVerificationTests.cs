using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.TenantSettings.Commands.UpdateTenantSettings;
using ROCloud.Application.Features.TenantSettings.Commands.VerifyUpiVpa;
using ROCloud.Domain.Entities.Platform;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;

namespace ROCloud.Application.Tests.TenantSettings;

/// <summary>
/// Verifying a UPI id — an OPTIONAL aid, not a gate. Razorpay's Validate VPA API was withdrawn with
/// the NPCI UPI-Collect deprecation, so no check can succeed for any tenant today; requiring one would
/// put the QR permanently out of reach.
///
/// <para>Where it does work, the check proves the id EXISTS and returns the name it is registered to; the owner confirms
/// that name is their own. The cases that matter are the negatives: "we could not check" must never be
/// reported as "your id is wrong", and a tick must never survive the id being edited.</para>
/// </summary>
public class UpiVerificationTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private const string Vpa = "dabhiro@okaxis";

    private static (AppDbContext Db, TenantContext Ctx) NewDb()
    {
        var ctx = new TenantContext { TenantId = TenantId };
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"upiver-{Guid.NewGuid()}").Options, ctx);
        return (db, ctx);
    }

    private static async Task SeedAsync(AppDbContext db, string? vpa = Vpa)
    {
        db.Tenants.Add(new Tenant
        {
            Id = TenantId, Name = "Dabhi RO Water", Subdomain = "dabhi",
            OwnerName = "O", OwnerEmail = "o@x.com", OwnerMobile = "9",
            Status = TenantStatus.Active, DefaultLanguage = "en", UpiVpa = vpa,
        });
        await db.SaveChangesAsync();
    }

    private static UpdateTenantSettingsCommand Settings(string? vpa) =>
        new("Dabhi RO Water", null, false, 18m, null, null, null, null, null, null, "en", UpiVpa: vpa);

    [Fact]
    public async Task AValidId_ReturnsTheRegisteredName_AndStampsTheTenant()
    {
        var (db, ctx) = NewDb();
        await SeedAsync(db);
        var razorpay = new FakeRazorpayService();
        razorpay.VpaResults[Vpa] = new RazorpayVpaValidation(true, "Kiran Kanzariya");

        var result = await new VerifyUpiVpaCommandHandler(db, ctx, razorpay)
            .Handle(new VerifyUpiVpaCommand(Vpa), CancellationToken.None);

        Assert.True(result.Verified);
        Assert.Equal("Kiran Kanzariya", result.PayeeName);

        var t = await db.Tenants.FirstAsync();
        Assert.NotNull(t.UpiVerifiedAt);
        Assert.Equal("Kiran Kanzariya", t.UpiVerifiedName);
    }

    [Fact]
    public async Task AnIdThatDoesNotExist_IsReportedAsUnverified_AndStampsNothing()
    {
        var (db, ctx) = NewDb();
        await SeedAsync(db);
        var razorpay = new FakeRazorpayService();
        razorpay.VpaResults[Vpa] = new RazorpayVpaValidation(false, null);

        var result = await new VerifyUpiVpaCommandHandler(db, ctx, razorpay)
            .Handle(new VerifyUpiVpaCommand(Vpa), CancellationToken.None);

        Assert.False(result.Verified);
        Assert.False(result.Unavailable);
        Assert.Null((await db.Tenants.FirstAsync()).UpiVerifiedAt);
    }

    [Fact]
    public async Task WhenTheCheckCannotRun_ItSaysSo_RatherThanCallingTheIdInvalid()
    {
        // No Razorpay credentials, network down, or the endpoint isn't enabled on the account. Telling
        // the owner their working UPI id is invalid would talk them out of a setup that would be fine.
        var (db, ctx) = NewDb();
        await SeedAsync(db);

        var result = await new VerifyUpiVpaCommandHandler(db, ctx, new FakeRazorpayService())
            .Handle(new VerifyUpiVpaCommand(Vpa), CancellationToken.None);

        Assert.True(result.Unavailable);
        Assert.False(result.Verified);
        Assert.Null((await db.Tenants.FirstAsync()).UpiVerifiedAt);
    }

    [Fact]
    public async Task VerifyingPersistsTheIdItChecked()
    {
        // The stamp has to describe an id that is actually stored, or the tick shown on reload would
        // vouch for something the tenant never saved. So verifying writes the id alongside its result.
        var (db, ctx) = NewDb();
        await SeedAsync(db, vpa: "old@okaxis");
        var razorpay = new FakeRazorpayService();
        razorpay.VpaResults["new@ybl"] = new RazorpayVpaValidation(true, "Kiran Kanzariya");

        var result = await new VerifyUpiVpaCommandHandler(db, ctx, razorpay)
            .Handle(new VerifyUpiVpaCommand("new@ybl"), CancellationToken.None);

        Assert.True(result.Verified);
        var t = await db.Tenants.FirstAsync();
        Assert.Equal("new@ybl", t.UpiVpa);
        Assert.NotNull(t.UpiVerifiedAt);
        Assert.False(t.UpiQrEnabled);       // verifying alone never switches the QR on
    }

    [Fact]
    public async Task TheQrCanBeEnabled_WithoutAnyCheck()
    {
        // Verification is OPTIONAL. Razorpay's Validate VPA API went away with the NPCI UPI-Collect
        // deprecation, so no check can succeed for anyone — requiring one would put the QR permanently
        // out of reach. The owner's opt-in plus the on-screen warning is the safeguard now.
        var (db, ctx) = NewDb();
        await SeedAsync(db);

        await new UpdateTenantSettingsCommandHandler(db, ctx)
            .Handle(Settings(Vpa) with { UpiQrEnabled = true }, CancellationToken.None);

        var t = await db.Tenants.FirstAsync();
        Assert.True(t.UpiQrEnabled);
        Assert.Null(t.UpiVerifiedAt);       // never checked, and that is allowed
    }

    [Fact]
    public async Task TheQrCanBeEnabled_OnceTheSavedIdIsVerified()
    {
        var (db, ctx) = NewDb();
        await SeedAsync(db, vpa: null);
        var razorpay = new FakeRazorpayService();
        razorpay.VpaResults[Vpa] = new RazorpayVpaValidation(true, "Kiran Kanzariya");

        // The real sequence: type → verify → enable → save.
        await new VerifyUpiVpaCommandHandler(db, ctx, razorpay)
            .Handle(new VerifyUpiVpaCommand(Vpa), CancellationToken.None);
        await new UpdateTenantSettingsCommandHandler(db, ctx)
            .Handle(Settings(Vpa) with { UpiQrEnabled = true }, CancellationToken.None);

        var t = await db.Tenants.FirstAsync();
        Assert.True(t.UpiQrEnabled);
        Assert.NotNull(t.UpiVerifiedAt);
    }

    [Fact]
    public async Task ChangingTheIdWhileTheQrIsOn_KeepsItOn_ButDropsTheOldTick()
    {
        // The QR follows the id: invoices issued from here pay the NEW id, and the tick that vouched
        // for the old one must not carry over onto it.
        var (db, ctx) = NewDb();
        await SeedAsync(db);
        var tenant = await db.Tenants.FirstAsync();
        tenant.UpiVerifiedAt = DateTime.UtcNow;
        tenant.UpiVerifiedName = "Kiran Kanzariya";
        tenant.UpiQrEnabled = true;
        await db.SaveChangesAsync();

        await new UpdateTenantSettingsCommandHandler(db, ctx)
            .Handle(Settings("someoneelse@ybl") with { UpiQrEnabled = true }, CancellationToken.None);

        var t = await db.Tenants.FirstAsync();
        Assert.Equal("someoneelse@ybl", t.UpiVpa);
        Assert.True(t.UpiQrEnabled);
        Assert.Null(t.UpiVerifiedAt);
    }

    [Fact]
    public async Task ChangingTheUpiId_ClearsThePreviousVerification()
    {
        var (db, ctx) = NewDb();
        await SeedAsync(db);
        var tenant = await db.Tenants.FirstAsync();
        tenant.UpiVerifiedAt = DateTime.UtcNow;
        tenant.UpiVerifiedName = "Kiran Kanzariya";
        await db.SaveChangesAsync();

        await new UpdateTenantSettingsCommandHandler(db, ctx)
            .Handle(Settings("someoneelse@ybl"), CancellationToken.None);

        var t = await db.Tenants.FirstAsync();
        Assert.Equal("someoneelse@ybl", t.UpiVpa);
        Assert.Null(t.UpiVerifiedAt);       // a tick must never outlive the id it vouched for
        Assert.Null(t.UpiVerifiedName);
    }

    [Fact]
    public async Task SavingTheSameId_KeepsItsVerification()
    {
        var (db, ctx) = NewDb();
        await SeedAsync(db);
        var tenant = await db.Tenants.FirstAsync();
        tenant.UpiVerifiedAt = DateTime.UtcNow;
        tenant.UpiVerifiedName = "Kiran Kanzariya";
        await db.SaveChangesAsync();

        // Editing an unrelated field must not throw away a good verification.
        await new UpdateTenantSettingsCommandHandler(db, ctx)
            .Handle(Settings(Vpa) with { City = "Surendranagar" }, CancellationToken.None);

        var t = await db.Tenants.FirstAsync();
        Assert.NotNull(t.UpiVerifiedAt);
        Assert.Equal("Kiran Kanzariya", t.UpiVerifiedName);
    }

    [Theory]
    [InlineData("dabhiro@okaxis", true)]
    [InlineData("dabhiro", false)]
    [InlineData("", false)]
    public void TheIdShapeIsCheckedBeforeSpendingANetworkCall(string vpa, bool valid)
        => Assert.Equal(valid, new VerifyUpiVpaCommandValidator().Validate(new VerifyUpiVpaCommand(vpa)).IsValid);
}
