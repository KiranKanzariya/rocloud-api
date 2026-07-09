namespace ROCloud.Application.Features.Platform.BackgroundJobs.Dtos;

/// <summary>Aggregate Hangfire counters (guide §14, §26).</summary>
public sealed record JobStatisticsDto(
    long Enqueued,
    long Scheduled,
    long Processing,
    long Succeeded,
    long Failed,
    long Deleted,
    long Recurring,
    long Servers);

/// <summary>A registered recurring job and its last/next run. <see cref="Enabled"/> reflects the
/// stored setting — a disabled job is paused (removed from the schedule) so has no next run.</summary>
public sealed record RecurringJobDto(
    string Id,
    string? Cron,
    string? Queue,
    DateTime? LastExecution,
    string? LastJobState,
    DateTime? NextExecution,
    string? Error,
    bool Enabled);

/// <summary>Outcome of editing a recurring job's cron.</summary>
public enum JobUpdateResult
{
    Ok,
    NotFound,
    InvalidCron,
}

/// <summary>Body for the edit-cron endpoint.</summary>
public sealed record UpdateCronRequest(string Cron);

/// <summary>An active Hangfire server (worker process).</summary>
public sealed record BackgroundServerDto(
    string Name,
    int WorkersCount,
    IReadOnlyList<string> Queues,
    DateTime? StartedAt,
    DateTime? Heartbeat);

/// <summary>Overview payload: whether Hangfire is up, its counters, recurring jobs and servers.</summary>
public sealed record JobOverviewDto(
    bool Enabled,
    JobStatisticsDto Statistics,
    IReadOnlyList<RecurringJobDto> RecurringJobs,
    IReadOnlyList<BackgroundServerDto> Servers);

/// <summary>A failed job with the exception that killed it.</summary>
public sealed record FailedJobDto(
    string Id,
    string Job,
    DateTime? FailedAt,
    string? ExceptionType,
    string? ExceptionMessage);
