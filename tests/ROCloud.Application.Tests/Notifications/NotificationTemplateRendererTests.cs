using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Services;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;

namespace ROCloud.Application.Tests.Notifications;

public class NotificationTemplateRendererTests
{
    private static readonly Guid TenantA = Guid.NewGuid();

    private static (AppDbContext Db, NotificationTemplateRenderer Renderer) New()
    {
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"notif-tpl-{Guid.NewGuid()}").Options,
            new TenantContext { TenantId = TenantA });
        return (db, new NotificationTemplateRenderer(db));
    }

    private static NotificationTemplate Row(
        Guid? tenantId, string code, string lang, string channel, string? subject, string body) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        TemplateCode = code,
        LanguageCode = lang,
        Channel = channel,
        Subject = subject,
        Body = body,
    };

    private static readonly Dictionary<string, string> NoTokens = new();

    [Fact]
    public async Task Returns_null_when_no_template_exists()
    {
        var (_, renderer) = New();
        var result = await renderer.RenderAsync(TenantA, "invoice_sent", "en", "Email", NoTokens);
        Assert.Null(result);
    }

    [Fact]
    public async Task Substitutes_tokens_case_insensitively_and_leaves_unknown_tokens()
    {
        var (db, renderer) = New();
        db.NotificationTemplates.Add(Row(
            null, "invoice_sent", "en", "Email",
            "Invoice {{InvoiceNumber}}",
            "Hi {{customername}}, see {{InvoiceNumber}} at {{DownloadUrl}}. {{Missing}}"));
        await db.SaveChangesAsync();

        var result = await renderer.RenderAsync(TenantA, "invoice_sent", "en", "Email",
            new Dictionary<string, string>
            {
                ["CustomerName"] = "Ravi",
                ["InvoiceNumber"] = "INV-1",
                ["DownloadUrl"] = "http://x/y",
            });

        Assert.NotNull(result);
        Assert.Equal("Invoice INV-1", result!.Subject);
        Assert.Equal("Hi Ravi, see INV-1 at http://x/y. {{Missing}}", result.Body);
    }

    [Fact]
    public async Task Tenant_override_wins_over_system_default()
    {
        var (db, renderer) = New();
        db.NotificationTemplates.Add(Row(null, "payment_reminder", "en", "WhatsApp", null, "DEFAULT"));
        db.NotificationTemplates.Add(Row(TenantA, "payment_reminder", "en", "WhatsApp", null, "TENANT"));
        await db.SaveChangesAsync();

        var result = await renderer.RenderAsync(TenantA, "payment_reminder", "en", "WhatsApp", NoTokens);

        Assert.Equal("TENANT", result!.Body);
    }

    [Fact]
    public async Task Requested_language_wins_and_falls_back_to_en_when_missing()
    {
        var (db, renderer) = New();
        db.NotificationTemplates.Add(Row(null, "amc_reminder", "en", "WhatsApp", null, "EN"));
        db.NotificationTemplates.Add(Row(null, "amc_reminder", "gu", "WhatsApp", null, "GU"));
        await db.SaveChangesAsync();

        var gu = await renderer.RenderAsync(TenantA, "amc_reminder", "gu", "WhatsApp", NoTokens);
        Assert.Equal("GU", gu!.Body);

        // Hindi row doesn't exist → fall back to the en default.
        var hi = await renderer.RenderAsync(TenantA, "amc_reminder", "hi", "WhatsApp", NoTokens);
        Assert.Equal("EN", hi!.Body);
    }

    [Fact]
    public async Task Does_not_leak_another_tenants_override()
    {
        var (db, renderer) = New();
        var otherTenant = Guid.NewGuid();
        db.NotificationTemplates.Add(Row(otherTenant, "invoice_sent", "en", "Email", "S", "OTHER"));
        await db.SaveChangesAsync();

        // Only another tenant's row exists — nothing for us or the system default.
        var result = await renderer.RenderAsync(TenantA, "invoice_sent", "en", "Email", NoTokens);
        Assert.Null(result);
    }
}
