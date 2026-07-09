using MediatR;
using ROCloud.Application.Common.Interfaces;

namespace ROCloud.Application.Features.Platform.BackgroundJobs.Commands.RequeueJob;

/// <summary>Requeue a (typically failed) job so a worker picks it up again (guide §14, §26).</summary>
public sealed record RequeueJobCommand(string JobId) : IRequest<bool>;

public class RequeueJobCommandHandler : IRequestHandler<RequeueJobCommand, bool>
{
    private readonly IBackgroundJobService _jobs;

    public RequeueJobCommandHandler(IBackgroundJobService jobs) => _jobs = jobs;

    public Task<bool> Handle(RequeueJobCommand request, CancellationToken ct)
        => Task.FromResult(_jobs.Requeue(request.JobId));
}
