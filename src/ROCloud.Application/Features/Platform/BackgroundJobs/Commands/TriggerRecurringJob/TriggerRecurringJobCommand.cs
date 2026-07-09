using MediatR;
using ROCloud.Application.Common.Interfaces;

namespace ROCloud.Application.Features.Platform.BackgroundJobs.Commands.TriggerRecurringJob;

/// <summary>Run a registered recurring job immediately (guide §14, §26).</summary>
public sealed record TriggerRecurringJobCommand(string RecurringJobId) : IRequest<bool>;

public class TriggerRecurringJobCommandHandler : IRequestHandler<TriggerRecurringJobCommand, bool>
{
    private readonly IBackgroundJobService _jobs;

    public TriggerRecurringJobCommandHandler(IBackgroundJobService jobs) => _jobs = jobs;

    public Task<bool> Handle(TriggerRecurringJobCommand request, CancellationToken ct)
        => Task.FromResult(_jobs.TriggerRecurring(request.RecurringJobId));
}
