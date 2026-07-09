using ROCloud.Application.Features.Platform.BackgroundJobs.Dtos;

namespace ROCloud.Application.Common.Interfaces;

/// <summary>
/// Reads and controls Hangfire background jobs for the super-admin portal (guide §14, §26).
/// Implemented in Infrastructure over Hangfire's monitoring API so the Application layer stays
/// free of a Hangfire dependency. Implementations must degrade gracefully (empty/false) when
/// Hangfire is not initialised.
/// </summary>
public interface IBackgroundJobService
{
    /// <summary>Counters, recurring jobs and active servers. <see cref="JobOverviewDto.Enabled"/>
    /// is false when Hangfire storage isn't available.</summary>
    JobOverviewDto GetOverview();

    /// <summary>The most recent failed jobs, newest first (capped by <paramref name="count"/>).</summary>
    IReadOnlyList<FailedJobDto> GetFailedJobs(int count);

    /// <summary>Requeue a job by id. Returns false if it doesn't exist or Hangfire is down.</summary>
    bool Requeue(string jobId);

    /// <summary>Run a recurring job now. Returns false if the id is unknown or Hangfire is down.</summary>
    bool TriggerRecurring(string recurringJobId);

    /// <summary>Enable (resume on its cron) or disable (pause) a recurring job, persisting the choice.
    /// Returns false if the id is not a known recurring job.</summary>
    bool SetRecurringEnabled(string recurringJobId, bool enabled);

    /// <summary>Change a recurring job's cron schedule, persisting it. Returns NotFound for an unknown
    /// id or InvalidCron for a malformed expression.</summary>
    JobUpdateResult UpdateRecurringCron(string recurringJobId, string cron);
}
