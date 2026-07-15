using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Subscription;
using ROCloud.Application.Features.Subscription.Services;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;

namespace ROCloud.Infrastructure.BackgroundJobs;

/// <summary>
/// Daily (09:00) tenant-billing housekeeping on the platform <c>tenants</c> table:
/// (1) flips Active tenants whose paid period has lapsed to Overdue (this is what arms the
/// middleware's payment-required block after Subscription:OverdueGraceDays and the auto-suspend
/// below); (2) reminds tenants whose trial/subscription ends within Jobs:SubscriptionExpiryWarnDays;
/// (3) suspends tenants that have been Overdue for Jobs:SubscriptionSuspendGraceDays+.
/// Operates platform-wide (tenants has no RLS).
/// </summary>
public class SubscriptionExpiryJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SubscriptionExpiryJob> _logger;
    private readonly int _warnDays;
    private readonly int _suspendGraceDays;
    private readonly int _reminderIntervalDays;
    private readonly int _invoiceLeadDays;

    public SubscriptionExpiryJob(
        IServiceScopeFactory scopeFactory, ILogger<SubscriptionExpiryJob> logger, IConfiguration config)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _warnDays = int.TryParse(config["Jobs:SubscriptionExpiryWarnDays"], out var w) ? w : 7;
        _suspendGraceDays = int.TryParse(config["Jobs:SubscriptionSuspendGraceDays"], out var s) ? s : 30;
        _reminderIntervalDays = int.TryParse(config["Jobs:SubscriptionExpiryReminderMinIntervalDays"], out var r) && r > 0 ? r : 3;
        _invoiceLeadDays = int.TryParse(config["Jobs:SubscriptionInvoiceLeadDays"], out var il) && il > 0 ? il : 5;
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var email = scope.ServiceProvider.GetRequiredService<IEmailService>();
        var templates = scope.ServiceProvider.GetRequiredService<INotificationTemplateRenderer>();
        var invoiceDelivery = scope.ServiceProvider.GetRequiredService<ISubscriptionInvoiceDelivery>();

        var now = DateTime.UtcNow;
        var soon = now.AddDays(_warnDays);
        var suspendCutoff = now.AddDays(-_suspendGraceDays);

        // 1. Mark lapsed tenants Overdue — a paid subscription past SubscriptionEndsAt OR a free
        //    trial past TrialEndsAt (both need the owner to pay). Saved before the suspend step so
        //    its query sees the freshly-overdue rows (EF queries hit the DB, not the change tracker).
        var toMarkOverdue = await db.Tenants
            .Where(t => (t.Status == TenantStatus.Active
                         && t.SubscriptionEndsAt != null && t.SubscriptionEndsAt < now)
                        || (t.Status == TenantStatus.Trial
                            && t.TrialEndsAt != null && t.TrialEndsAt < now))
            .ToListAsync(ct);

        foreach (var tenant in toMarkOverdue)
            tenant.Status = TenantStatus.Overdue;

        if (toMarkOverdue.Count > 0)
            await db.SaveChangesAsync(ct);

        // 2. Expiry reminders — trial or subscription ending within the next 7 days.
        var expiring = await db.Tenants
            .Where(t => (t.Status == TenantStatus.Trial || t.Status == TenantStatus.Active || t.Status == TenantStatus.Overdue)
                        && ((t.TrialEndsAt != null && t.TrialEndsAt >= now && t.TrialEndsAt <= soon)
                            || (t.SubscriptionEndsAt != null && t.SubscriptionEndsAt >= now && t.SubscriptionEndsAt <= soon)))
            .Select(t => new { t.Id, t.OwnerEmail, t.Name })
            .ToListAsync(ct);

        // Cadence throttle so re-runs (and daily runs through the window) don't re-email an owner who
        // was reminded within the interval. reminder_log is tenant-scoped, but this job is platform-wide
        // (no tenant GUC set), so read past the query filter and match on subject_id = tenant id.
        var reminderCutoff = now.AddDays(-_reminderIntervalDays);
        var recentlyReminded = (await db.ReminderLogs.IgnoreQueryFilters()
            .Where(r => r.ReminderType == ReminderTypes.SubscriptionExpiry && r.CreatedAt >= reminderCutoff)
            .Select(r => r.SubjectId)
            .ToListAsync(ct)).ToHashSet();

        var reminded = 0;
        foreach (var t in expiring)
        {
            if (string.IsNullOrWhiteSpace(t.OwnerEmail) || recentlyReminded.Contains(t.Id)) continue;
            try
            {
                var tokens = new Dictionary<string, string>
                {
                    ["TenantName"] = t.Name,
                    ["Days"] = _warnDays.ToString(),
                };
                // Platform dunning notice — use the shared system default (tenant_id NULL).
                var rendered = await templates.RenderAsync(null, "subscription_expiry", "en", "Email", tokens, ct);
                var subject = rendered?.Subject ?? "Your ROCloud subscription is expiring soon";
                var body = rendered?.Body
                    ?? $"Hi {t.Name}, your ROCloud subscription expires within {_warnDays} days. Please renew to avoid interruption.";
                await email.SendAsync(t.OwnerEmail, subject, body, ct);

                db.ReminderLogs.Add(new ReminderLog
                {
                    Id = Guid.NewGuid(), TenantId = t.Id, ReminderType = ReminderTypes.SubscriptionExpiry,
                    SubjectId = t.Id, CustomerId = null, Channel = "Email",
                });
                recentlyReminded.Add(t.Id);
                reminded++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "SubscriptionExpiry: reminder failed for {Email}", t.OwnerEmail);
            }
        }

        if (reminded > 0)
            await db.SaveChangesAsync(ct);

        // 2b. Renewals — for a subscription within the invoice lead window (default 5 days) or already
        //     lapsed (Active/Overdue only; Trial/Cancelled/Suspended are skipped). For a payable plan
        //     (net > 0) raise a Pending invoice for the SAME plan (Option A, full price) for the owner
        //     to pay. For a fully-discounted / free plan (net ₹0) there is nothing to pay, so
        //     AUTO-RENEW instead: record a ₹0 Paid invoice and roll the term forward one cycle,
        //     keeping the tenant Active so a comped account never wrongly lapses/suspends. Idempotent:
        //     a tenant with an open Pending invoice is skipped (also backstopped by
        //     ux_subscription_invoices_open_period).
        var invoiceHorizon = now.AddDays(_invoiceLeadDays);
        var toInvoice = await db.Tenants.Include(t => t.Plan)
            .Where(t => (t.Status == TenantStatus.Active || t.Status == TenantStatus.Overdue)
                        && t.SubscriptionEndsAt != null
                        && t.SubscriptionEndsAt <= invoiceHorizon)
            .ToListAsync(ct);

        var tenantsWithOpenInvoice = (await db.SubscriptionInvoices
            .Where(i => i.Status == SubscriptionInvoiceStatus.Pending)
            .Select(i => i.TenantId)
            .ToListAsync(ct)).ToHashSet();

        var invoicesCreated = 0;
        var autoRenewed = 0;
        foreach (var t in toInvoice)
        {
            if (t.Plan is null || tenantsWithOpenInvoice.Contains(t.Id)) continue;

            var yearly = string.Equals(
                await SubscriptionInvoiceFactory.LatestBillingCycleAsync(db, t.Id, ct),
                "Yearly", StringComparison.OrdinalIgnoreCase);
            var cycle = yearly ? "Yearly" : "Monthly";
            var gross = yearly ? t.Plan.YearlyPrice : t.Plan.MonthlyPrice;
            var net = SubscriptionDiscountCalculator.Net(t.SubscriptionDiscountType, t.SubscriptionDiscountValue, gross);
            var unit = yearly ? "year" : "month";

            if (net <= 0m)
            {
                // Free / fully-discounted → nothing to pay: auto-renew. Roll the term forward one cycle
                // (Option A basis) and keep the tenant Active, with a ₹0 Paid invoice for the record.
                var basis = t.SubscriptionEndsAt is { } end && end > now ? end : now;
                var freeInvoice = await SubscriptionInvoiceFactory.BuildAsync(
                    db, t, t.Plan, cycle, DateOnly.FromDateTime(basis), SubscriptionInvoiceStatus.Paid,
                    $"{t.Plan.Name} plan — 1 {unit} (free renewal)", ct);
                db.SubscriptionInvoices.Add(freeInvoice);   // no email; its PDF renders on demand
                t.SubscriptionEndsAt = yearly ? basis.AddYears(1) : basis.AddMonths(1);
                t.Status = TenantStatus.Active;
                autoRenewed++;
                continue;
            }

            // Payable → raise a Pending invoice for the owner to pay.
            var invoice = await SubscriptionInvoiceFactory.BuildAsync(
                db, t, t.Plan, cycle, DateOnly.FromDateTime(t.SubscriptionEndsAt!.Value),
                SubscriptionInvoiceStatus.Pending, $"{t.Plan.Name} plan — 1 {unit} renewal", ct);
            db.SubscriptionInvoices.Add(invoice);
            await invoiceDelivery.IssueAsync(invoice, t, ct);   // email owner the invoice (best-effort)
            tenantsWithOpenInvoice.Add(t.Id);
            invoicesCreated++;
        }

        // Persist before the suspend step below, so its DB query sees auto-renewed tenants as Active.
        if (invoicesCreated > 0 || autoRenewed > 0)
            await db.SaveChangesAsync(ct);

        // 3. Suspend tenants that have been Overdue for 30+ days — measured from the effective end
        //    date (paid end, or trial end for a tenant that lapsed straight from trial).
        var toSuspend = await db.Tenants
            .Where(t => t.Status == TenantStatus.Overdue
                        && (t.SubscriptionEndsAt ?? t.TrialEndsAt) != null
                        && (t.SubscriptionEndsAt ?? t.TrialEndsAt) < suspendCutoff)
            .ToListAsync(ct);

        foreach (var tenant in toSuspend)
            tenant.Status = TenantStatus.Suspended;

        if (toSuspend.Count > 0)
            await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "SubscriptionExpiry: {Overdue} marked overdue, {Reminders} reminder(s) sent, {Invoices} renewal invoice(s) raised, {AutoRenewed} free tenant(s) auto-renewed, {Suspended} tenant(s) suspended",
            toMarkOverdue.Count, reminded, invoicesCreated, autoRenewed, toSuspend.Count);
    }
}
