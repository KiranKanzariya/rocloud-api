using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Models;
using ROCloud.Application.Features.AmcSubscriptions.Dtos;
using ROCloud.Domain.Entities.Tenant;

namespace ROCloud.Application.Features.AmcSubscriptions.Queries.GetAmcSubscriptions;

public sealed record GetAmcSubscriptionsQuery(AmcSubscriptionFilterDto Filter)
    : IRequest<PagedResult<AmcSubscriptionListItemDto>>;

public class GetAmcSubscriptionsQueryHandler
    : IRequestHandler<GetAmcSubscriptionsQuery, PagedResult<AmcSubscriptionListItemDto>>
{
    private readonly IAppDbContext _db;

    public GetAmcSubscriptionsQueryHandler(IAppDbContext db) => _db = db;

    public async Task<PagedResult<AmcSubscriptionListItemDto>> Handle(
        GetAmcSubscriptionsQuery request, CancellationToken ct)
    {
        var f = request.Filter;
        var page = Math.Max(1, f.Page);
        var pageSize = Math.Clamp(f.PageSize, 1, 100);

        IQueryable<AmcSubscription> query = _db.AmcSubscriptions;

        if (f.CustomerId is { } customerId) query = query.Where(s => s.CustomerId == customerId);
        if (f.IsActive is { } isActive) query = query.Where(s => s.IsActive == isActive);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderBy(s => s.NextDueDate).ThenByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(s => new AmcSubscriptionListItemDto(
                s.Id,
                s.CustomerId,
                s.Customer != null ? s.Customer.Name : string.Empty,
                s.Customer != null ? s.Customer.Mobile : null,
                s.PlanName,
                s.IntervalMonths,
                s.Amount,
                s.StartDate,
                s.EndDate,
                s.LastServiceDate,
                s.NextDueDate,
                s.IsActive))
            .ToListAsync(ct);

        return new PagedResult<AmcSubscriptionListItemDto>(items, total, page, pageSize);
    }
}
