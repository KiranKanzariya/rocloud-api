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
/// Weekly (Monday 09:00) reminders for service requests / AMC visits scheduled within the next
/// Jobs:AmcReminderLeadDays, for every active tenant. Delivers by WhatsApp when it is enabled globally
/// (Notifications:WhatsAppEnabled) AND the tenant's plan includes it AND the customer has a mobile;
/// otherwise falls back to email when email is enabled globally and one is on file. Throttled by
/// cadence: a visit already reminded within Jobs:AmcReminderMinIntervalDays (default 7) is skipped, so
/// a visit that stays in the lead window across consecutive runs isn't reminded twice (reminder_log).
/// </summary>
public class AmcReminderJob
{
    private static readonly ServiceRequestStatus[] OpenStatuses =
        [ServiceRequestStatus.Open, ServiceRequestStatus.InProgress];

    private readonly TenantJobRunner _runner;
    private readonly ILogger<AmcReminderJob> _logger;
    private readonly IAppSettings _settings;
    private readonly int _leadDays;
    private readonly int _minIntervalDays;

    public AmcReminderJob(
        TenantJobRunner runner, ILogger<AmcReminderJob> logger, IAppSettings settings, IConfiguration config)
    {
        _runner = runner;
        _logger = logger;
        _settings = settings;
        _leadDays = int.TryParse(config["Jobs:AmcReminderLeadDays"], out var d) ? d : 7;
        _minIntervalDays = int.TryParse(config["Jobs:AmcReminderMinIntervalDays"], out var m) && m > 0 ? m : 7;
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        if (!_settings.AmcReminderEnabled)
        {
            _logger.LogInformation("AmcReminder: disabled via Notifications:CustomerNotifications:AmcReminder — skipping");
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
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

            // Cadence throttle: visits already reminded within the interval are skipped this run.
            var cutoff = DateTime.UtcNow.AddDays(-_minIntervalDays);
            var recentlyReminded = (await db.ReminderLogs
                .Where(r => r.ReminderType == ReminderTypes.Amc && r.CreatedAt >= cutoff)
                .Select(r => r.SubjectId)
                .ToListAsync(token)).ToHashSet();

            // Customer-facing mail is branded with the tenant's business, not ROCloud (§14, matches SendInvoice).
            var businessName = await db.Tenants
                .Where(t => t.Id == tenantId).Select(t => t.Name).FirstOrDefaultAsync(token);
            if (!string.IsNullOrWhiteSpace(businessName))
                brand.Current = new EmailBrand(businessName);

            var upcoming = await db.ServiceRequests
                .Where(s => s.ScheduledDate != null
                            && s.ScheduledDate >= today && s.ScheduledDate <= until
                            && OpenStatuses.Contains(s.Status))
                .Select(s => new
                {
                    s.Id,
                    s.TicketNumber,
                    s.ScheduledDate,
                    CustomerId = s.Customer != null ? s.Customer.Id : (Guid?)null,
                    CustomerName = s.Customer != null ? s.Customer.Name : string.Empty,
                    CustomerMobile = s.Customer != null ? s.Customer.Mobile : null,
                    CustomerEmail = s.Customer != null ? s.Customer.Email : null,
                    CustomerLanguage = s.Customer != null ? s.Customer.PreferredLanguage : null
                })
                .ToListAsync(token);

            var sent = 0;
            foreach (var sr in upcoming)
            {
                if (recentlyReminded.Contains(sr.Id)) continue; // reminded recently — skip

                var tokens = new Dictionary<string, string>
                {
                    ["CustomerName"] = sr.CustomerName,
                    ["TicketNumber"] = sr.TicketNumber,
                    ["ScheduledDate"] = sr.ScheduledDate?.ToString("dd MMM yyyy") ?? string.Empty,
                };
                var defaultBody =
                    $"Hi {sr.CustomerName}, your service visit ({sr.TicketNumber}) is scheduled for " +
                    $"{sr.ScheduledDate:dd MMM yyyy}. Our technician will reach you. Thank you.";

                string? channelUsed = null;
                if (whatsAppEnabled && !string.IsNullOrWhiteSpace(sr.CustomerMobile))
                {
                    var rendered = await templates.RenderAsync(
                        tenantId, "amc_reminder", sr.CustomerLanguage, "WhatsApp", tokens, token);
                    await whatsapp.SendAsync(sr.CustomerMobile, rendered?.Body ?? defaultBody, token);
                    channelUsed = "WhatsApp";
                }
                else if (settings.EmailEnabled && !string.IsNullOrWhiteSpace(sr.CustomerEmail))
                {
                    var rendered = await templates.RenderAsync(
                        tenantId, "amc_reminder", sr.CustomerLanguage, "Email", tokens, token);
                    var subject = rendered?.Subject ?? "Upcoming service visit reminder";
                    await email.SendAsync(sr.CustomerEmail, subject, rendered?.Body ?? defaultBody, token);
                    channelUsed = "Email";
                }

                if (channelUsed is not null)
                {
                    db.ReminderLogs.Add(new ReminderLog
                    {
                        Id = Guid.NewGuid(), TenantId = tenantId, ReminderType = ReminderTypes.Amc,
                        SubjectId = sr.Id, CustomerId = sr.CustomerId, Channel = channelUsed,
                    });
                    recentlyReminded.Add(sr.Id);
                    sent++;
                }
            }

            await db.SaveChangesAsync(token);
            _logger.LogInformation("AmcReminder: tenant {TenantId} reminded {Count} customer(s)", tenantId, sent);
        }, ct);
    }
}
