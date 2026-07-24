using ROCloud.Application.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;

namespace ROCloud.Application.Features.CustomerSubscriptions.Commands.CancelCustomerSubscription;

/// <summary>Ends a customer's delivery subscription so the rollover job stops generating its orders.</summary>
public sealed record CancelCustomerSubscriptionCommand(Guid Id) : IRequest;

public class CancelCustomerSubscriptionCommandHandler : IRequestHandler<CancelCustomerSubscriptionCommand>
{
    private readonly IAppDbContext _db;

    public CancelCustomerSubscriptionCommandHandler(IAppDbContext db) => _db = db;

    public async Task Handle(CancelCustomerSubscriptionCommand request, CancellationToken ct)
    {
        var subscription = await _db.CustomerSubscriptions
            .FirstOrDefaultAsync(s => s.Id == request.Id, ct)
            ?? throw new NotFoundException("CustomerSubscription", request.Id);

        subscription.IsActive = false;
        subscription.EndDate = AppTimeZone.Today(DateTime.UtcNow);
        await _db.SaveChangesAsync(ct);
    }
}
