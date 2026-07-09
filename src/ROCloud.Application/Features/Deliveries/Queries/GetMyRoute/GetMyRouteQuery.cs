using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Deliveries.Dtos;
using ROCloud.Application.Features.Deliveries.Queries.GetDeliveries;

namespace ROCloud.Application.Features.Deliveries.Queries.GetMyRoute;

/// <summary>
/// The current delivery boy's own route — restricted to deliveries assigned to them.
/// Backs the mobile route view (requires Deliveries.ViewOwn).
/// </summary>
public sealed record GetMyRouteQuery(DeliveryFilterDto Filter) : IRequest<IReadOnlyList<DeliveryListItemDto>>;

public class GetMyRouteQueryHandler : IRequestHandler<GetMyRouteQuery, IReadOnlyList<DeliveryListItemDto>>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public GetMyRouteQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<DeliveryListItemDto>> Handle(GetMyRouteQuery request, CancellationToken ct)
    {
        var userId = _currentUser.UserId
                     ?? throw new ForbiddenAccessException();

        // Force the own-deliveries restriction regardless of any DeliveryBoyId in the filter.
        var filter = request.Filter with { DeliveryBoyId = userId };

        var query = GetDeliveriesQueryHandler.ApplyFilter(_db.Deliveries, filter)
            .Where(d => d.DeliveryBoyId == userId);

        return await query
            .OrderBy(d => d.ScheduledDate).ThenBy(d => d.Id)
            .ToListItem(_db)
            .ToListAsync(ct);
    }
}
