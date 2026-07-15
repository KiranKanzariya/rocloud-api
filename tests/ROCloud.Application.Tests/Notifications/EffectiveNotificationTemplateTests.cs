using FluentValidation.TestHelper;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Features.NotificationTemplates;
using ROCloud.Application.Features.NotificationTemplates.Commands.DeleteNotificationTemplate;
using ROCloud.Application.Features.NotificationTemplates.Commands.UpsertNotificationTemplate;
using ROCloud.Application.Features.NotificationTemplates.Queries.GetNotificationTemplates;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;

namespace ROCloud.Application.Tests.Notifications;

/// <summary>The owner sees the EFFECTIVE template: its own override if any, else the system default.</summary>
public class EffectiveNotificationTemplateTests
{
    private static (AppDbContext Db, TenantContext Ctx) NewDb()
    {
        var ctx = new TenantContext { TenantId = Guid.NewGuid() };
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"ent-{Guid.NewGuid()}").Options, ctx);
        return (db, ctx);
    }

    private static NotificationTemplate Row(Guid? tenantId, string code, string body) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId,
        TemplateCode = code, LanguageCode = "en", Channel = "Email", Body = body
    };

    [Fact]
    public async Task NoOverride_ReturnsDefault_FlaggedNotCustom()
    {
        var (db, ctx) = NewDb();
        db.NotificationTemplates.Add(Row(null, "invoice_sent", "System default"));
        await db.SaveChangesAsync();

        var result = await new GetNotificationTemplatesQueryHandler(db, ctx)
            .Handle(new GetNotificationTemplatesQuery(), CancellationToken.None);

        var row = Assert.Single(result);
        Assert.Equal("System default", row.Body);
        Assert.False(row.IsCustom);
    }

    [Fact]
    public async Task Override_WinsOverDefault_FlaggedCustom_NoDuplicate()
    {
        var (db, ctx) = NewDb();
        db.NotificationTemplates.Add(Row(null, "invoice_sent", "System default"));
        db.NotificationTemplates.Add(Row(ctx.TenantId, "invoice_sent", "My version"));
        await db.SaveChangesAsync();

        var result = await new GetNotificationTemplatesQueryHandler(db, ctx)
            .Handle(new GetNotificationTemplatesQuery(), CancellationToken.None);

        var row = Assert.Single(result);   // one effective row, not both
        Assert.Equal("My version", row.Body);
        Assert.True(row.IsCustom);
    }

    [Theory]
    [InlineData("welcome")]
    [InlineData("welcome_google")]
    [InlineData("password_reset")]
    [InlineData("subscription_expiry")]
    [InlineData("subscription_invoice")]
    [InlineData("subscription_receipt")]
    public async Task PlatformOnlyTemplate_IsHiddenFromTenant(string platformCode)
    {
        // ROCloud sends these to the owner, not the owner to their customers. They render with a null
        // tenant id, so an override is never read — showing them would promise an edit that does nothing.
        var (db, ctx) = NewDb();
        db.NotificationTemplates.Add(Row(null, platformCode, "Sent by ROCloud"));
        db.NotificationTemplates.Add(Row(null, "invoice_sent", "Invoice"));
        await db.SaveChangesAsync();

        var result = await new GetNotificationTemplatesQueryHandler(db, ctx)
            .Handle(new GetNotificationTemplatesQuery(), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("invoice_sent", result[0].TemplateCode);
    }

    [Fact]
    public async Task OnlyCustomerFacingTemplates_SurviveTheFilter()
    {
        var (db, ctx) = NewDb();
        foreach (var code in PlatformTemplates.Codes)
            db.NotificationTemplates.Add(Row(null, code, "Sent by ROCloud"));
        foreach (var code in new[] { "invoice_sent", "payment_reminder", "amc_reminder", "advance_order_reminder" })
            db.NotificationTemplates.Add(Row(null, code, "Sent by the business"));
        await db.SaveChangesAsync();

        var result = await new GetNotificationTemplatesQueryHandler(db, ctx)
            .Handle(new GetNotificationTemplatesQuery(), CancellationToken.None);

        Assert.Equal(
            ["advance_order_reminder", "amc_reminder", "invoice_sent", "payment_reminder"],
            result.Select(r => r.TemplateCode).OrderBy(c => c));
    }

    [Theory]
    [InlineData("welcome")]
    [InlineData("password_reset")]
    [InlineData("subscription_receipt")]
    public void SavingAnOverrideForAPlatformTemplate_IsRejected(string platformCode)
    {
        // Hiding it from the list is not enough: a hand-made request would otherwise store a row the
        // owner believes is live, when the send path would never look at it.
        var result = new UpsertNotificationTemplateCommandValidator().TestValidate(
            new UpsertNotificationTemplateCommand(platformCode, "en", "Email", "Subject", "Body"));

        result.ShouldHaveValidationErrorFor(c => c.TemplateCode);
    }

    [Fact]
    public void SavingAnOverrideForACustomerTemplate_IsAllowed()
    {
        var result = new UpsertNotificationTemplateCommandValidator().TestValidate(
            new UpsertNotificationTemplateCommand("invoice_sent", "en", "Email", "Subject", "Body"));

        result.ShouldNotHaveValidationErrorFor(c => c.TemplateCode);
    }

    [Fact]
    public async Task Delete_RemovesOverride_RevertsToSystemDefault()
    {
        var (db, ctx) = NewDb();
        db.NotificationTemplates.Add(Row(null, "invoice_sent", "System default"));
        var overrideRow = Row(ctx.TenantId, "invoice_sent", "My version");
        db.NotificationTemplates.Add(overrideRow);
        await db.SaveChangesAsync();

        await new DeleteNotificationTemplateCommandHandler(db, ctx)
            .Handle(new DeleteNotificationTemplateCommand(overrideRow.Id), CancellationToken.None);

        var result = await new GetNotificationTemplatesQueryHandler(db, ctx)
            .Handle(new GetNotificationTemplatesQuery(), CancellationToken.None);
        var row = Assert.Single(result);
        Assert.Equal("System default", row.Body);   // reverted to the default
        Assert.False(row.IsCustom);
    }

    [Fact]
    public async Task Delete_CannotRemoveSystemDefault()
    {
        var (db, ctx) = NewDb();
        var def = Row(null, "invoice_sent", "System default");
        db.NotificationTemplates.Add(def);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<NotFoundException>(() => new DeleteNotificationTemplateCommandHandler(db, ctx)
            .Handle(new DeleteNotificationTemplateCommand(def.Id), CancellationToken.None));
        Assert.Single(await db.NotificationTemplates.ToListAsync());   // default untouched
    }
}
