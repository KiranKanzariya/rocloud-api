using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROCloud.API.Filters;
using ROCloud.Application.Common.Models;
using ROCloud.Application.Features.Platform.BackgroundJobs.Commands.RequeueJob;
using ROCloud.Application.Features.Platform.BackgroundJobs.Commands.SetJobEnabled;
using ROCloud.Application.Features.Platform.BackgroundJobs.Commands.TriggerRecurringJob;
using ROCloud.Application.Features.Platform.BackgroundJobs.Commands.UpdateJobCron;
using ROCloud.Application.Features.Platform.BackgroundJobs.Dtos;
using ROCloud.Application.Features.Platform.BackgroundJobs.Queries.GetFailedJobs;
using ROCloud.Application.Features.Platform.BackgroundJobs.Queries.GetJobOverview;

namespace ROCloud.API.Controllers.Platform;

/// <summary>
/// Hangfire background-job monitoring &amp; control for the super-admin portal (guide §14, §26).
/// A native in-portal view over Hangfire's monitoring API — an alternative to the /hangfire
/// dashboard that reuses the normal bearer-token + platform-role auth. SuperAdmin only.
/// </summary>
[ApiController]
[Route("api/platform/background-jobs")]
[Authorize]
[RequirePlatformRole("SuperAdmin")]
public class PlatformBackgroundJobsController : ControllerBase
{
    private readonly IMediator _mediator;

    public PlatformBackgroundJobsController(IMediator mediator) => _mediator = mediator;

    /// <summary>Counters, recurring jobs and active servers.</summary>
    [HttpGet]
    public async Task<IActionResult> GetOverview(CancellationToken ct)
        => Ok(ApiResponse<JobOverviewDto>.Ok(await _mediator.Send(new GetJobOverviewQuery(), ct)));

    /// <summary>The most recent failed jobs.</summary>
    [HttpGet("failed")]
    public async Task<IActionResult> GetFailed([FromQuery] int count, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<FailedJobDto>>.Ok(
            await _mediator.Send(new GetFailedJobsQuery(count <= 0 ? 50 : count), ct)));

    /// <summary>Requeue a (typically failed) job by id.</summary>
    [HttpPost("{jobId}/requeue")]
    public async Task<IActionResult> Requeue(string jobId, CancellationToken ct)
    {
        var ok = await _mediator.Send(new RequeueJobCommand(jobId), ct);
        return ok
            ? Ok(ApiResponse<object>.Ok(new { requeued = true }))
            : NotFound(ApiResponse<object>.Fail("Job not found or could not be requeued.", "JOB_NOT_FOUND"));
    }

    /// <summary>Run a recurring job immediately.</summary>
    [HttpPost("recurring/{recurringJobId}/trigger")]
    public async Task<IActionResult> Trigger(string recurringJobId, CancellationToken ct)
    {
        var ok = await _mediator.Send(new TriggerRecurringJobCommand(recurringJobId), ct);
        return ok
            ? Ok(ApiResponse<object>.Ok(new { triggered = true }))
            : NotFound(ApiResponse<object>.Fail("Recurring job not found.", "RECURRING_JOB_NOT_FOUND"));
    }

    /// <summary>Resume a paused recurring job (runs on its cron again).</summary>
    [HttpPost("recurring/{recurringJobId}/enable")]
    public Task<IActionResult> Enable(string recurringJobId, CancellationToken ct)
        => SetEnabled(recurringJobId, true, ct);

    /// <summary>Pause a recurring job (removed from the schedule until re-enabled).</summary>
    [HttpPost("recurring/{recurringJobId}/disable")]
    public Task<IActionResult> Disable(string recurringJobId, CancellationToken ct)
        => SetEnabled(recurringJobId, false, ct);

    private async Task<IActionResult> SetEnabled(string recurringJobId, bool enabled, CancellationToken ct)
    {
        var ok = await _mediator.Send(new SetJobEnabledCommand(recurringJobId, enabled), ct);
        return ok
            ? Ok(ApiResponse<object>.Ok(new { enabled }))
            : NotFound(ApiResponse<object>.Fail("Recurring job not found.", "RECURRING_JOB_NOT_FOUND"));
    }

    /// <summary>Change a recurring job's cron schedule.</summary>
    [HttpPut("recurring/{recurringJobId}/cron")]
    public async Task<IActionResult> UpdateCron(string recurringJobId, [FromBody] UpdateCronRequest body, CancellationToken ct)
    {
        var result = await _mediator.Send(new UpdateJobCronCommand(recurringJobId, body.Cron), ct);
        return result switch
        {
            JobUpdateResult.Ok => Ok(ApiResponse<object>.Ok(new { cron = body.Cron.Trim() })),
            JobUpdateResult.InvalidCron => BadRequest(ApiResponse<object>.Fail("Invalid cron expression.", "INVALID_CRON")),
            _ => NotFound(ApiResponse<object>.Fail("Recurring job not found.", "RECURRING_JOB_NOT_FOUND")),
        };
    }
}
