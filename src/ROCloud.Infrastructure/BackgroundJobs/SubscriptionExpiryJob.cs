using System.Globalization;
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
/// (3) suspends tenants that have been Overdue for Jobs:SubscriptionSuspendGraceDays+ (a paid lapse)
/// or Jobs:TrialSuspendGraceDays+ (a never-paid trial).
/// Operates platform-wide (tenants has no RLS).
/// </summary>
public class SubscriptionExpiryJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SubscriptionExpiryJob> _logger;
    private readonly int _warnDays;
    private readonly int _suspendGraceDays;
    private readonly int _trialSuspendGraceDays;
    private readonly int _overdueGraceDays;
    private readonly int _reminderIntervalDays;
    private readonly int _invoiceLeadDays;

    public SubscriptionExpiryJob(
        IServiceScopeFactory scopeFactory, ILogger<SubscriptionExpiryJob> logger, IConfiguration config)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _warnDays = int.TryParse(config["Jobs:SubscriptionExpiryWarnDays"], out var w) ? w : 7;
        _suspendGraceDays = int.TryParse(config["Jobs:SubscriptionSuspendGraceDays"], out var s) ? s : 30;
        _trialSuspendGraceDays = int.TryParse(config["Jobs:TrialSuspendGraceDays"], out var ts) ? ts : 7;
        // Must match TenantMiddleware — it decides which days a lapsed tenant could actually use.
        _overdueGraceDays = int.TryParse(config["Subscription:OverdueGraceDays"], out var og) ? og : 7;
        _reminderIntervalDays = int.TryParse(config["Jobs:SubscriptionExpiryReminderMinIntervalDays"], out var r) && r > 0 ? r : 3;
        _invoiceLeadDays = int.TryParse(config["Jobs:SubscriptionInvoiceLeadDays"], out var il) && il > 0 ? il : 5;
    }

    /// <summary>
    /// "free trial" / "subscription" in the tenant's language. The label is substituted rather than kept
    /// in two template rows so the three languages stay one row each per event.
    /// </summary>
    private static string PlanLabel(string language, bool isTrial) => (language, isTrial) switch
    {
        ("hi", true) => "निःशुल्क परीक्षण",
        ("hi", false) => "सदस्यता",
        ("gu", true) => "મફત ટ્રાયલ",
        ("gu", false) => "સબસ્ક્રિપ્શન",
        (_, true) => "free trial",
        (_, false) => "subscription",
    };

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
        var trialSuspendCutoff = now.AddDays(-_trialSuspendGraceDays);

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
            .Select(t => new { t.Id, t.OwnerEmail, t.Name, t.TrialEndsAt, t.SubscriptionEndsAt, t.DefaultLanguage })
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
                // A tenant on a trial has no SubscriptionEndsAt, so whichever date is set is the one
                // running out. Never say "subscription" to a trial owner — they bought nothing, so
                // there is nothing to "renew"; they need to choose a plan.
                var isTrial = t.SubscriptionEndsAt is null;
                var endsAt = t.SubscriptionEndsAt ?? t.TrialEndsAt!.Value;
                var language = string.IsNullOrWhiteSpace(t.DefaultLanguage) ? "en" : t.DefaultLanguage;

                var tokens = new Dictionary<string, string>
                {
                    ["TenantName"] = t.Name,
                    ["PlanLabel"] = PlanLabel(language, isTrial),
                    ["EndDate"] = endsAt.ToString("dd MMM yyyy", CultureInfo.InvariantCulture),
                    // Real days remaining, not the config window — the old code passed the warn setting,
                    // so an owner one day from expiry was still told "within 7 days". Kept for any
                    // template that wants a countdown; the shipped wording uses the date, which cannot
                    // go stale if the mail is read late.
                    ["Days"] = Math.Max(0, (endsAt.Date - now.Date).Days).ToString(),
                };
                // Platform dunning notice — the shared system default (tenant_id NULL), in the tenant's
                // own language. This used to be hardcoded "en", leaving the seeded hi/gu rows unusable.
                var rendered = await templates.RenderAsync(null, "subscription_expiry", language, "Email", tokens, ct);
                // Fallbacks mirror the seeded English template, so a missing row degrades to the same
                // wording rather than to a different (and staler) message.
                var endDate = endsAt.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
                var subject = rendered?.Subject ?? $"Your ROCloud {PlanLabel("en", isTrial)} expires on {endDate}";
                var body = rendered?.Body
                    ?? $"Hi {t.Name}, your ROCloud {PlanLabel("en", isTrial)} expires on {endDate}. "
                       + "Sign in to keep your workspace running.";
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
                t.SubscriptionEndsAt = SubscriptionTermCalculator.NextEnd(
                    t.SubscriptionEndsAt, yearly, _overdueGraceDays, now);
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

        // 3. Suspend tenants that have been Overdue past their suspend grace, measured from the effective
        //    end date. The two lapse types get different windows, matching what is at stake:
        //      • PAID lapse (SubscriptionEndsAt set) → 30 days. A real business with live data, staff and
        //        routes should not be hard-suspended over one late payment; the month is a dunning window.
        //      • Never-paid TRIAL (no SubscriptionEndsAt) → 7 days. They have paid nothing and have been
        //        fully blocked since TrialGraceDays (2), so a month in Overdue buys them nothing — it only
        //        blurs the platform dashboard, where a ghosted trial looks the same as a late customer.
        //    A tenant with neither date set is never suspended: both comparisons are null-false, which
        //    preserves the old explicit null guard.
        var toSuspend = await db.Tenants
            .Where(t => t.Status == TenantStatus.Overdue
                        && (t.SubscriptionEndsAt != null
                                ? t.SubscriptionEndsAt < suspendCutoff
                                : t.TrialEndsAt < trialSuspendCutoff))
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
