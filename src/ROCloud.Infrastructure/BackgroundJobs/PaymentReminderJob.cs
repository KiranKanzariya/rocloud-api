using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Settings;
using ROCloud.Application.Features.Payments.Queries.GetOutstandingDues;
using ROCloud.Application.Features.Subscription;
using ROCloud.Domain.Entities.Tenant;

namespace ROCloud.Infrastructure.BackgroundJobs;

/// <summary>
/// Daily (10:00) reminders to customers with invoices overdue by more than 7 days, for every
/// active tenant. Delivers by WhatsApp when it is enabled globally (Notifications:WhatsAppEnabled)
/// AND the tenant's plan includes it AND the customer has a mobile; otherwise falls back to email
/// when email is enabled globally and the customer has an email on file. Throttled by cadence: a
/// customer is skipped if they were reminded within Jobs:PaymentReminderMinIntervalDays (default 3),
/// so a lingering overdue invoice doesn't nag every day (recorded in reminder_log).
/// </summary>
public class PaymentReminderJob
{
    private readonly TenantJobRunner _runner;
    private readonly ILogger<PaymentReminderJob> _logger;
    private readonly IAppSettings _settings;
    private readonly int _minIntervalDays;

    public PaymentReminderJob(
        TenantJobRunner runner, ILogger<PaymentReminderJob> logger, IAppSettings settings, IConfiguration config)
    {
        _runner = runner;
        _logger = logger;
        _settings = settings;
        _minIntervalDays = int.TryParse(config["Jobs:PaymentReminderMinIntervalDays"], out var d) && d > 0 ? d : 3;
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        if (!_settings.PaymentReminderEnabled)
        {
            _logger.LogInformation("PaymentReminder: disabled via Notifications:CustomerNotifications:PaymentReminder — skipping");
            return;
        }

        await _runner.ForEachTenantAsync(async (sp, tenantId, token) =>
        {
            var db = sp.GetRequiredService<IAppDbContext>();
            var settings = sp.GetRequiredService<IAppSettings>();
            // WhatsApp: master switch first, then the per-tenant plan feature.
            var whatsAppEnabled = settings.WhatsAppEnabled
                                  && await PlanFeatures.WhatsAppEnabledAsync(db, tenantId, token);

            var mediator = sp.GetRequiredService<IMediator>();
            var whatsapp = sp.GetRequiredService<IWhatsAppService>();
            var email = sp.GetRequiredService<IEmailService>();
            var brand = sp.GetRequiredService<IEmailBrandContext>();
            var templates = sp.GetRequiredService<INotificationTemplateRenderer>();

            // Cadence throttle: customers already reminded within the interval are skipped this run.
            var cutoff = DateTime.UtcNow.AddDays(-_minIntervalDays);
            var recentlyReminded = (await db.ReminderLogs
                .Where(r => r.ReminderType == ReminderTypes.Payment && r.CreatedAt >= cutoff)
                .Select(r => r.SubjectId)
                .ToListAsync(token)).ToHashSet();

            // Customer-facing mail is branded with the tenant's business, not ROCloud (§14, matches SendInvoice).
            var businessName = await db.Tenants
                .Where(t => t.Id == tenantId).Select(t => t.Name).FirstOrDefaultAsync(token);
            if (!string.IsNullOrWhiteSpace(businessName))
                brand.Current = new EmailBrand(businessName);

            var dues = await mediator.Send(new GetOutstandingDuesQuery(7), token);
            var sent = 0;
            foreach (var due in dues)
            {
                if (recentlyReminded.Contains(due.CustomerId)) continue; // reminded recently — skip

                var tokens = new Dictionary<string, string>
                {
                    ["CustomerName"] = due.CustomerName,
                    ["Amount"] = due.OutstandingAmount.ToString("N2"),
                    ["DaysOverdue"] = due.DaysOverdue.ToString(),
                };
                var defaultBody =
                    $"Hi {due.CustomerName}, you have ₹{due.OutstandingAmount:N2} outstanding " +
                    $"({due.DaysOverdue} days overdue). Please clear your dues. Thank you.";

                string? channelUsed = null;
                // channelUsed is only set when the provider actually accepted the message. It gates the
                // reminder_log write below, and that row suppresses re-reminding this customer for
                // PaymentReminderMinIntervalDays — so recording a send that silently failed would mean
                // the customer is never chased at all. A failure here leaves no row, and the next run
                // retries.
                if (whatsAppEnabled && !string.IsNullOrWhiteSpace(due.CustomerMobile))
                {
                    var rendered = await templates.RenderAsync(
                        tenantId, "payment_reminder", due.CustomerLanguage, "WhatsApp", tokens, token);
                    if (await whatsapp.SendAsync(due.CustomerMobile, rendered?.Body ?? defaultBody, token))
                        channelUsed = "WhatsApp";
                }
                else if (settings.EmailEnabled && !string.IsNullOrWhiteSpace(due.CustomerEmail))
                {
                    var rendered = await templates.RenderAsync(
                        tenantId, "payment_reminder", due.CustomerLanguage, "Email", tokens, token);
                    var subject = rendered?.Subject ?? "Payment reminder";
                    if (await email.SendAsync(due.CustomerEmail, subject, rendered?.Body ?? defaultBody, token))
                        channelUsed = "Email";
                }

                if (channelUsed is not null)
                {
                    db.ReminderLogs.Add(new ReminderLog
                    {
                        Id = Guid.NewGuid(), TenantId = tenantId, ReminderType = ReminderTypes.Payment,
                        SubjectId = due.CustomerId, CustomerId = due.CustomerId, Channel = channelUsed,
                    });
                    recentlyReminded.Add(due.CustomerId);
                    sent++;
                }
            }

            await db.SaveChangesAsync(token);
            _logger.LogInformation("PaymentReminder: tenant {TenantId} reminded {Count} customer(s)", tenantId, sent);
        }, ct);
    }
}
