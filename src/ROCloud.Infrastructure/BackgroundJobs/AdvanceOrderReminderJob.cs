using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Settings;
using ROCloud.Application.Features.Subscription;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;

namespace ROCloud.Infrastructure.BackgroundJobs;

/// <summary>
/// Daily (08:00 IST) day-before reminder for Advance bookings (event/program orders). For every
/// active tenant it finds Advance orders whose delivery day falls within the next
/// Jobs:AdvanceOrderReminderLeadDays (default 1 = tomorrow) and are still open, then sends the
/// customer a heads-up. This is the nudge the customer asked for when they booked ahead — nobody
/// otherwise contacts them between booking and the delivery day. Delivered by WhatsApp when it is
/// enabled globally (Notifications:WhatsAppEnabled) AND the tenant's plan includes it AND the customer
/// has a mobile; otherwise falls back to email when email is enabled globally. Throttled by cadence:
/// an order already reminded within Jobs:AdvanceOrderReminderMinIntervalDays (default 7) is skipped, so
/// a wider lead window never reminds the same order twice (reminder_log).
/// </summary>
public class AdvanceOrderReminderJob
{
    private static readonly OrderStatus[] OpenStatuses = [OrderStatus.Pending, OrderStatus.Confirmed];

    private readonly TenantJobRunner _runner;
    private readonly ILogger<AdvanceOrderReminderJob> _logger;
    private readonly IAppSettings _settings;
    private readonly int _leadDays;
    private readonly int _minIntervalDays;

    public AdvanceOrderReminderJob(
        TenantJobRunner runner, ILogger<AdvanceOrderReminderJob> logger, IAppSettings settings, IConfiguration config)
    {
        _runner = runner;
        _logger = logger;
        _settings = settings;
        _leadDays = int.TryParse(config["Jobs:AdvanceOrderReminderLeadDays"], out var d) && d > 0 ? d : 1;
        _minIntervalDays = int.TryParse(config["Jobs:AdvanceOrderReminderMinIntervalDays"], out var m) && m > 0 ? m : 7;
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        if (!_settings.AdvanceOrderReminderEnabled)
        {
            _logger.LogInformation("AdvanceOrderReminder: disabled via Notifications:CustomerNotifications:AdvanceOrderReminder — skipping");
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = today.AddDays(1);            // day-before → tomorrow onward, never today
        var until = today.AddDays(_leadDays);

        await _runner.ForEachTenantAsync(async (sp, tenantId, token) =>
        {
            var db = sp.GetRequiredService<IAppDbContext>();
            var settings = sp.GetRequiredService<IAppSettings>();
            // WhatsApp: master switch first, then the per-tenant plan feature.
            var whatsAppEnabled = settings.WhatsAppEnabled
                                  && await PlanFeatures.WhatsAppEnabledAsync(db, tenantId, token);

            var whatsapp = sp.GetRequiredService<IWhatsAppService>();
            var email = sp.GetRequiredService<IEmailService>();
            var brand = sp.GetRequiredService<IEmailBrandContext>();
            var templates = sp.GetRequiredService<INotificationTemplateRenderer>();

            // Cadence throttle: orders already reminded within the interval are skipped this run.
            var cutoff = DateTime.UtcNow.AddDays(-_minIntervalDays);
            var recentlyReminded = (await db.ReminderLogs
                .Where(r => r.ReminderType == ReminderTypes.AdvanceOrder && r.CreatedAt >= cutoff)
                .Select(r => r.SubjectId)
                .ToListAsync(token)).ToHashSet();

            // Customer-facing mail is branded with the tenant's business, not ROCloud (§14, matches SendInvoice).
            var businessName = await db.Tenants
                .Where(t => t.Id == tenantId).Select(t => t.Name).FirstOrDefaultAsync(token);
            if (!string.IsNullOrWhiteSpace(businessName))
                brand.Current = new EmailBrand(businessName);

            var upcoming = await db.Orders
                .Where(o => o.OrderType == OrderType.Advance
                            && o.OrderDate >= from && o.OrderDate <= until
                            && OpenStatuses.Contains(o.Status))
                .Select(o => new
                {
                    o.Id,
                    o.OrderDate,
                    CustomerId = o.Customer != null ? o.Customer.Id : (Guid?)null,
                    CustomerName = o.Customer != null ? o.Customer.Name : string.Empty,
                    CustomerMobile = o.Customer != null ? o.Customer.Mobile : null,
                    CustomerEmail = o.Customer != null ? o.Customer.Email : null,
                    CustomerLanguage = o.Customer != null ? o.Customer.PreferredLanguage : null,
                    Quantity = o.OrderItems.Sum(i => (int?)i.Quantity) ?? 0
                })
                .ToListAsync(token);

            var sent = 0;
            foreach (var o in upcoming)
            {
                if (recentlyReminded.Contains(o.Id)) continue; // reminded recently — skip

                var tokens = new Dictionary<string, string>
                {
                    ["CustomerName"] = o.CustomerName,
                    ["ScheduledDate"] = o.OrderDate.ToString("dd MMM yyyy"),
                    ["Quantity"] = o.Quantity.ToString(),
                };
                var defaultBody =
                    $"Hi {o.CustomerName}, a reminder that your order of {o.Quantity} item(s) is " +
                    $"scheduled for {o.OrderDate:dd MMM yyyy}. We'll have it ready. Thank you.";

                string? channelUsed = null;
                if (whatsAppEnabled && !string.IsNullOrWhiteSpace(o.CustomerMobile))
                {
                    var rendered = await templates.RenderAsync(
                        tenantId, "advance_order_reminder", o.CustomerLanguage, "WhatsApp", tokens, token);
                    await whatsapp.SendAsync(o.CustomerMobile, rendered?.Body ?? defaultBody, token);
                    channelUsed = "WhatsApp";
                }
                else if (settings.EmailEnabled && !string.IsNullOrWhiteSpace(o.CustomerEmail))
                {
                    var rendered = await templates.RenderAsync(
                        tenantId, "advance_order_reminder", o.CustomerLanguage, "Email", tokens, token);
                    var subject = rendered?.Subject ?? "Upcoming order reminder";
                    await email.SendAsync(o.CustomerEmail, subject, rendered?.Body ?? defaultBody, token);
                    channelUsed = "Email";
                }

                if (channelUsed is not null)
                {
                    db.ReminderLogs.Add(new ReminderLog
                    {
                        Id = Guid.NewGuid(), TenantId = tenantId, ReminderType = ReminderTypes.AdvanceOrder,
                        SubjectId = o.Id, CustomerId = o.CustomerId, Channel = channelUsed,
                    });
                    recentlyReminded.Add(o.Id);
                    sent++;
                }
            }

            await db.SaveChangesAsync(token);
            _logger.LogInformation(
                "AdvanceOrderReminder: tenant {TenantId} reminded {Count} customer(s)", tenantId, sent);
        }, ct);
    }
}
