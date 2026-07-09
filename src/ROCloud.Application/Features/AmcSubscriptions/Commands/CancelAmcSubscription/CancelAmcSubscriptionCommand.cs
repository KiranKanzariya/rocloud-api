using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;

namespace ROCloud.Application.Features.AmcSubscriptions.Commands.CancelAmcSubscription;

/// <summary>Deactivates an AMC subscription (kept for history; excluded from scheduling).</summary>
public sealed record CancelAmcSubscriptionCommand(Guid Id) : IRequest;

public class CancelAmcSubscriptionCommandHandler : IRequestHandler<CancelAmcSubscriptionCommand>
{
    private readonly IAppDbContext _db;

    public CancelAmcSubscriptionCommandHandler(IAppDbContext db) => _db = db;

    public async Task Handle(CancelAmcSubscriptionCommand request, CancellationToken ct)
    {
        var subscription = await _db.AmcSubscriptions.FirstOrDefaultAsync(s => s.Id == request.Id, ct)
                           ?? throw new NotFoundException("AmcSubscription", request.Id);

        subscription.IsActive = false;
        await _db.SaveChangesAsync(ct);
    }
}
