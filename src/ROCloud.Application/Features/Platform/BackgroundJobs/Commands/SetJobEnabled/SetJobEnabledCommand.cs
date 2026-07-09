using MediatR;
using ROCloud.Application.Common.Interfaces;

namespace ROCloud.Application.Features.Platform.BackgroundJobs.Commands.SetJobEnabled;

/// <summary>Enable (resume) or disable (pause) a recurring job (guide §14, §26).</summary>
public sealed record SetJobEnabledCommand(string RecurringJobId, bool Enabled) : IRequest<bool>;

public class SetJobEnabledCommandHandler : IRequestHandler<SetJobEnabledCommand, bool>
{
    private readonly IBackgroundJobService _jobs;

    public SetJobEnabledCommandHandler(IBackgroundJobService jobs) => _jobs = jobs;

    public Task<bool> Handle(SetJobEnabledCommand request, CancellationToken ct)
        => Task.FromResult(_jobs.SetRecurringEnabled(request.RecurringJobId, request.Enabled));
}
