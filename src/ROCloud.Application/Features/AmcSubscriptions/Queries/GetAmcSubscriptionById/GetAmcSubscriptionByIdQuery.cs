using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.AmcSubscriptions.Dtos;

namespace ROCloud.Application.Features.AmcSubscriptions.Queries.GetAmcSubscriptionById;

public sealed record GetAmcSubscriptionByIdQuery(Guid Id) : IRequest<AmcSubscriptionListItemDto>;

public class GetAmcSubscriptionByIdQueryHandler
    : IRequestHandler<GetAmcSubscriptionByIdQuery, AmcSubscriptionListItemDto>
{
    private readonly IAppDbContext _db;

    public GetAmcSubscriptionByIdQueryHandler(IAppDbContext db) => _db = db;

    public async Task<AmcSubscriptionListItemDto> Handle(GetAmcSubscriptionByIdQuery request, CancellationToken ct)
    {
        var s = await _db.AmcSubscriptions
            .Include(x => x.Customer)
            .FirstOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new NotFoundException("AmcSubscription", request.Id);

        return new AmcSubscriptionListItemDto(
            s.Id, s.CustomerId, s.Customer?.Name ?? string.Empty, s.Customer?.Mobile,
            s.PlanName, s.IntervalMonths, s.Amount, s.StartDate, s.EndDate,
            s.LastServiceDate, s.NextDueDate, s.IsActive);
    }
}
