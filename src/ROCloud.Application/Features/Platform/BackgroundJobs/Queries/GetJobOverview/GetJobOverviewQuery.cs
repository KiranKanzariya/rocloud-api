using MediatR;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Platform.BackgroundJobs.Dtos;

namespace ROCloud.Application.Features.Platform.BackgroundJobs.Queries.GetJobOverview;

/// <summary>Hangfire counters, recurring jobs and active servers (guide §14, §26).</summary>
public sealed record GetJobOverviewQuery : IRequest<JobOverviewDto>;

public class GetJobOverviewQueryHandler : IRequestHandler<GetJobOverviewQuery, JobOverviewDto>
{
    private readonly IBackgroundJobService _jobs;

    public GetJobOverviewQueryHandler(IBackgroundJobService jobs) => _jobs = jobs;

    public Task<JobOverviewDto> Handle(GetJobOverviewQuery request, CancellationToken ct)
        => Task.FromResult(_jobs.GetOverview());
}
