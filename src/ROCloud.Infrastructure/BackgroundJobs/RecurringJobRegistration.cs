using Hangfire;
using ROCloud.Application.Common;

namespace ROCloud.Infrastructure.BackgroundJobs;

/// <summary>
/// The catalogue of recurring background jobs (guide §14) and how to (re)register them with Hangfire.
/// <see cref="Descriptors"/> is the single source of truth for job id, default cron and the typed
/// method call — reused at startup (<see cref="Register"/>) and at runtime when a job is enabled or
/// its cron edited from the super-admin portal. Per-job overrides live in recurring_job_settings.
/// </summary>
public static class RecurringJobRegistration
{
    /// <summary>A known recurring job: its id, the built-in default cron, and how to (re)register it.</summary>
    /// <param name="Apply">Registers/updates the job in Hangfire with the given cron.</param>
    /// <param name="DefaultEnabled">
    /// Whether the job runs on a FRESH install (no settings row yet). Defaults to true; set false for a
    /// job tied to a deferred feature so it stays off until a SuperAdmin turns it on. Once a settings
    /// row exists, that stored value wins — this only decides the first-seen default.
    /// </param>
    public sealed record JobDescriptor(string Id, string DefaultCron, Action<string> Apply, bool DefaultEnabled = true);

    // All crons below are wall-clock in the app timezone (App:TimeZone, default IST). Hangfire defaults
    // to UTC when no TimeZone is supplied, so we pin every recurring job to that zone — otherwise e.g.
    // "9 AM" fires at 09:00 UTC (2:30 PM IST) and the nightly rollover ("23:59") actually runs at 05:29
    // the next morning. Built per apply (not a static field) so the zone reflects AppTimeZone.Configure()
    // without static-initialisation ordering worries.
    private static RecurringJobOptions JobOptions() => new() { TimeZone = AppTimeZone.Current };

    public static readonly IReadOnlyList<JobDescriptor> Descriptors = new List<JobDescriptor>
    {
        new("monthly-billing", "30 0 1 * *", cron => RecurringJob.AddOrUpdate<MonthlyBillingJob>(
            "monthly-billing", j => j.ExecuteAsync(CancellationToken.None), cron, JobOptions())),
        new("daily-delivery-rollover", "59 23 * * *", cron => RecurringJob.AddOrUpdate<DailyDeliveryRolloverJob>(
            "daily-delivery-rollover", j => j.ExecuteAsync(CancellationToken.None), cron, JobOptions())),
        new("subscription-expiry", "0 9 * * *", cron => RecurringJob.AddOrUpdate<SubscriptionExpiryJob>(
            "subscription-expiry", j => j.ExecuteAsync(CancellationToken.None), cron, JobOptions())),
        new("payment-reminder", "0 10 * * *", cron => RecurringJob.AddOrUpdate<PaymentReminderJob>(
            "payment-reminder", j => j.ExecuteAsync(CancellationToken.None), cron, JobOptions())),
        // Safety net + backfill for the invoice paid-amount/status materialisation. Runs before the
        // 10:00 payment reminder, so a customer who has in fact paid is never dunned that morning.
        new("invoice-allocation-sync", "0 2 * * *", cron => RecurringJob.AddOrUpdate<InvoiceAllocationSyncJob>(
            "invoice-allocation-sync", j => j.ExecuteAsync(CancellationToken.None), cron, JobOptions())),
        // AMC / Service is deferred to a future release (the owner portal hides it), so this reminder
        // is OFF on a fresh install — otherwise it would email customers about a feature they can't see.
        // A SuperAdmin can enable it from the Background Jobs page when the module ships.
        new("amc-reminder", "0 9 * * 1", cron => RecurringJob.AddOrUpdate<AmcReminderJob>(
            "amc-reminder", j => j.ExecuteAsync(CancellationToken.None), cron, JobOptions()), DefaultEnabled: false),
        new("advance-order-reminder", "0 8 * * *", cron => RecurringJob.AddOrUpdate<AdvanceOrderReminderJob>(
            "advance-order-reminder", j => j.ExecuteAsync(CancellationToken.None), cron, JobOptions())),
        new("audit-log-partition", "0 0 25 * *", cron => RecurringJob.AddOrUpdate<AuditLogPartitionJob>(
            "audit-log-partition", j => j.ExecuteAsync(CancellationToken.None), cron, JobOptions())),
        new("audit-log-retention", "0 1 26 * *", cron => RecurringJob.AddOrUpdate<AuditLogRetentionJob>(
            "audit-log-retention", j => j.ExecuteAsync(CancellationToken.None), cron, JobOptions())),
        new("log-retention", "0 3 * * *", cron => RecurringJob.AddOrUpdate<LogRetentionJob>(
            "log-retention", j => j.ExecuteAsync(CancellationToken.None), cron, JobOptions())),
        new("payment-reconciliation", "*/15 * * * *", cron => RecurringJob.AddOrUpdate<PaymentReconciliationJob>(
            "payment-reconciliation", j => j.ExecuteAsync(CancellationToken.None), cron, JobOptions())),
    };

    /// <summary>
    /// Called once at startup. For each known job: seed its settings row if missing, then apply the
    /// stored cron when enabled, or remove it from the schedule when disabled. Falls back to the
    /// built-in default cron + <see cref="JobDescriptor.DefaultEnabled"/> when no settings row exists yet.
    /// </summary>
    public static void Register(RecurringJobSettingsStore store)
    {
        var settings = store.GetAll();
        foreach (var d in Descriptors)
        {
            if (!settings.TryGetValue(d.Id, out var s))
            {
                store.SeedIfMissing(d.Id, d.DefaultCron, d.DefaultEnabled);
                s = new RecurringJobSettingsStore.Setting(d.Id, d.DefaultCron, d.DefaultEnabled);
            }

            if (s.Enabled)
                d.Apply(s.Cron);
            else
                RecurringJob.RemoveIfExists(d.Id);
        }
    }
}
