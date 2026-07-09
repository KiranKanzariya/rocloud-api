using MediatR;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Platform.BackgroundJobs.Dtos;

namespace ROCloud.Application.Features.Platform.BackgroundJobs.Commands.UpdateJobCron;

/// <summary>Change a recurring job's cron schedule (guide §14, §26).</summary>
public sealed record UpdateJobCronCommand(string RecurringJobId, string Cron) : IRequest<JobUpdateResult>;

public class UpdateJobCronCommandHandler : IRequestHandler<UpdateJobCronCommand, JobUpdateResult>
{
    private readonly IBackgroundJobService _jobs;

    public UpdateJobCronCommandHandler(IBackgroundJobService jobs) => _jobs = jobs;

    public Task<JobUpdateResult> Handle(UpdateJobCronCommand request, CancellationToken ct)
        => Task.FromResult(_jobs.UpdateRecurringCron(request.RecurringJobId, request.Cron));
}
