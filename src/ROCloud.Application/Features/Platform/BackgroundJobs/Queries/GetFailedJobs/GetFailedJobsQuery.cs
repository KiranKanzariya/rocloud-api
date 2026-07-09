using MediatR;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Platform.BackgroundJobs.Dtos;

namespace ROCloud.Application.Features.Platform.BackgroundJobs.Queries.GetFailedJobs;

/// <summary>The most recent failed jobs (guide §14, §26). Count is clamped to a sane range.</summary>
public sealed record GetFailedJobsQuery(int Count = 50) : IRequest<IReadOnlyList<FailedJobDto>>;

public class GetFailedJobsQueryHandler : IRequestHandler<GetFailedJobsQuery, IReadOnlyList<FailedJobDto>>
{
    private readonly IBackgroundJobService _jobs;

    public GetFailedJobsQueryHandler(IBackgroundJobService jobs) => _jobs = jobs;

    public Task<IReadOnlyList<FailedJobDto>> Handle(GetFailedJobsQuery request, CancellationToken ct)
        => Task.FromResult(_jobs.GetFailedJobs(Math.Clamp(request.Count, 1, 200)));
}
