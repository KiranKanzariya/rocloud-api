using System.Text.RegularExpressions;
using Hangfire;
using Hangfire.Common;
using Hangfire.Storage;
using Microsoft.Extensions.Logging;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Platform.BackgroundJobs.Dtos;
// Alias our DTO — Hangfire.Storage also defines a RecurringJobDto.
using AppRecurringJobDto = ROCloud.Application.Features.Platform.BackgroundJobs.Dtos.RecurringJobDto;

namespace ROCloud.Infrastructure.BackgroundJobs;

/// <summary>
/// Reads and controls Hangfire jobs via its monitoring API for the super-admin portal
/// (guide §14, §26) — the data behind the in-portal "Background Jobs" page, an alternative to
/// the raw /hangfire dashboard. Accesses <see cref="JobStorage.Current"/> statically (rather than
/// via DI) so it degrades to a disabled/empty view when Hangfire isn't initialised. Per-job cron and
/// enabled overrides are persisted via <see cref="RecurringJobSettingsStore"/>; the recurring-job
/// catalogue comes from <see cref="RecurringJobRegistration.Descriptors"/> so disabled jobs (removed
/// from Hangfire storage) still appear in the list.
/// </summary>
public partial class HangfireJobService : IBackgroundJobService
{
    private static readonly JobStatisticsDto EmptyStats = new(0, 0, 0, 0, 0, 0, 0, 0);

    // 5-field (min hour dom mon dow) or 6-field (with seconds) cron; per-field chars only.
    [GeneratedRegex(@"^\s*([\d*/,\-?LW#]+\s+){4,5}[\d*/,\-?LW#]+\s*$")]
    private static partial Regex CronShapeRegex();

    private readonly RecurringJobSettingsStore _store;
    private readonly ILogger<HangfireJobService> _logger;

    public HangfireJobService(RecurringJobSettingsStore store, ILogger<HangfireJobService> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>JobStorage.Current throws if Hangfire was never configured; treat that as "down".</summary>
    private static JobStorage? Storage
    {
        get
        {
            try { return JobStorage.Current; }
            catch (InvalidOperationException) { return null; }
        }
    }

    public JobOverviewDto GetOverview()
    {
        var storage = Storage;
        if (storage is null)
            return new JobOverviewDto(false, EmptyStats, [], []);

        var monitor = storage.GetMonitoringApi();
        var s = monitor.GetStatistics();
        var stats = new JobStatisticsDto(
            s.Enqueued, s.Scheduled, s.Processing, s.Succeeded,
            s.Failed, s.Deleted, s.Recurring, s.Servers);

        var servers = monitor.Servers()
            .Select(sv => new BackgroundServerDto(
                sv.Name, sv.WorkersCount, sv.Queues?.ToList() ?? [], sv.StartedAt, sv.Heartbeat))
            .ToList();

        var settings = _store.GetAll();
        using var conn = storage.GetConnection();
        var live = conn.GetRecurringJobs().ToDictionary(r => r.Id, StringComparer.Ordinal);

        // Build the list from the known catalogue so disabled (removed-from-Hangfire) jobs still show.
        var recurring = RecurringJobRegistration.Descriptors
            .Select(d =>
            {
                settings.TryGetValue(d.Id, out var setting);
                live.TryGetValue(d.Id, out var h);
                var enabled = setting?.Enabled ?? true;
                var cron = setting?.Cron ?? h?.Cron ?? d.DefaultCron;
                return new AppRecurringJobDto(
                    d.Id, cron, h?.Queue, h?.LastExecution, h?.LastJobState,
                    enabled ? h?.NextExecution : null, h?.Error, enabled);
            })
            .OrderBy(r => r.Id, StringComparer.Ordinal)
            .ToList();

        return new JobOverviewDto(true, stats, recurring, servers);
    }

    public IReadOnlyList<FailedJobDto> GetFailedJobs(int count)
    {
        var storage = Storage;
        if (storage is null) return [];

        return storage.GetMonitoringApi().FailedJobs(0, count)
            .Select(kv => new FailedJobDto(
                kv.Key,
                DescribeJob(kv.Value.Job),
                kv.Value.FailedAt,
                kv.Value.ExceptionType,
                kv.Value.ExceptionMessage))
            .ToList();
    }

    public bool Requeue(string jobId)
    {
        if (Storage is null || string.IsNullOrWhiteSpace(jobId)) return false;
        try
        {
            return BackgroundJob.Requeue(jobId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Requeue failed for job {JobId}", jobId);
            return false;
        }
    }

    public bool TriggerRecurring(string recurringJobId)
    {
        var storage = Storage;
        if (storage is null || string.IsNullOrWhiteSpace(recurringJobId)) return false;

        using var conn = storage.GetConnection();
        if (!conn.GetRecurringJobs().Any(r => r.Id == recurringJobId)) return false;

        try
        {
            RecurringJob.TriggerJob(recurringJobId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Trigger failed for recurring job {RecurringJobId}", recurringJobId);
            return false;
        }
    }

    public bool SetRecurringEnabled(string recurringJobId, bool enabled)
    {
        var desc = FindDescriptor(recurringJobId);
        if (desc is null || Storage is null) return false;

        var cron = _store.GetAll().GetValueOrDefault(recurringJobId)?.Cron ?? desc.DefaultCron;
        _store.SeedIfMissing(recurringJobId, cron);
        _store.SetEnabled(recurringJobId, enabled);

        try
        {
            if (enabled) desc.Apply(cron);
            else RecurringJob.RemoveIfExists(recurringJobId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Applying enabled={Enabled} failed for {RecurringJobId}", enabled, recurringJobId);
            return false;
        }
        return true;
    }

    public JobUpdateResult UpdateRecurringCron(string recurringJobId, string cron)
    {
        var desc = FindDescriptor(recurringJobId);
        if (desc is null || Storage is null) return JobUpdateResult.NotFound;

        cron = cron?.Trim() ?? "";
        if (!CronShapeRegex().IsMatch(cron)) return JobUpdateResult.InvalidCron;

        var enabled = _store.GetAll().GetValueOrDefault(recurringJobId)?.Enabled ?? true;

        // For an enabled job, let Hangfire deep-validate the cron before we persist it.
        if (enabled)
        {
            try
            {
                desc.Apply(cron);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Hangfire rejected cron '{Cron}' for {RecurringJobId}", cron, recurringJobId);
                return JobUpdateResult.InvalidCron;
            }
        }

        _store.SeedIfMissing(recurringJobId, cron);
        _store.SetCron(recurringJobId, cron);
        return JobUpdateResult.Ok;
    }

    private static RecurringJobRegistration.JobDescriptor? FindDescriptor(string id)
        => RecurringJobRegistration.Descriptors.FirstOrDefault(d => d.Id == id);

    private static string DescribeJob(Job? job)
        => job is null ? "(unknown)" : $"{job.Type.Name}.{job.Method.Name}";
}
