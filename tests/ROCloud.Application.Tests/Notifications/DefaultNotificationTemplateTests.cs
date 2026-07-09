using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Sanitisation;
using ROCloud.Application.Features.Platform.NotificationTemplates.Commands.UpsertDefaultNotificationTemplate;
using ROCloud.Application.Features.Platform.NotificationTemplates.Queries.GetDefaultNotificationTemplates;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;

namespace ROCloud.Application.Tests.Notifications;

/// <summary>The admin-side default-template editor writes/reads only the NULL-tenant baseline rows.</summary>
public class DefaultNotificationTemplateTests
{
    private static AppDbContext NewDb()
    {
        var ctx = new TenantContext { TenantId = Guid.NewGuid() };
        return new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"dnt-{Guid.NewGuid()}").Options, ctx);
    }

    [Fact]
    public async Task Upsert_SameTuple_UpdatesTheOneNullTenantRow()
    {
        var db = NewDb();
        var handler = new UpsertDefaultNotificationTemplateCommandHandler(db, new EmailHtmlSanitizerService());

        var id1 = await handler.Handle(
            new UpsertDefaultNotificationTemplateCommand("payment_reminder", "en", "WhatsApp", null, "First"),
            CancellationToken.None);
        var id2 = await handler.Handle(
            new UpsertDefaultNotificationTemplateCommand("payment_reminder", "en", "WhatsApp", null, "Second"),
            CancellationToken.None);

        Assert.Equal(id1, id2);   // same (NULL tenant, code, lang, channel) → updated, not duplicated
        var rows = await db.NotificationTemplates.Where(t => t.TenantId == null).ToListAsync();
        Assert.Single(rows);
        Assert.Equal("Second", rows[0].Body);
        Assert.Null(rows[0].TenantId);
    }

    [Fact]
    public async Task Query_ReturnsOnlyDefaults_NotTenantOverrides()
    {
        var db = NewDb();
        db.NotificationTemplates.Add(new NotificationTemplate
        {
            Id = Guid.NewGuid(), TenantId = null,
            TemplateCode = "welcome", LanguageCode = "en", Channel = "Email", Body = "Default"
        });
        db.NotificationTemplates.Add(new NotificationTemplate
        {
            Id = Guid.NewGuid(), TenantId = Guid.NewGuid(),
            TemplateCode = "welcome", LanguageCode = "en", Channel = "Email", Body = "Tenant override"
        });
        await db.SaveChangesAsync();

        var result = await new GetDefaultNotificationTemplatesQueryHandler(db)
            .Handle(new GetDefaultNotificationTemplatesQuery(), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("Default", result[0].Body);
    }
}
