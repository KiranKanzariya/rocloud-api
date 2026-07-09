using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Models;
using ROCloud.Application.Features.Deliveries.Dtos;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Deliveries.Queries.GetDeliveries;

public sealed record GetDeliveriesQuery(DeliveryFilterDto Filter) : IRequest<PagedResult<DeliveryListItemDto>>;

public class GetDeliveriesQueryHandler : IRequestHandler<GetDeliveriesQuery, PagedResult<DeliveryListItemDto>>
{
    private readonly IAppDbContext _db;

    public GetDeliveriesQueryHandler(IAppDbContext db) => _db = db;

    public async Task<PagedResult<DeliveryListItemDto>> Handle(GetDeliveriesQuery request, CancellationToken ct)
    {
        var f = request.Filter;
        var page = Math.Max(1, f.Page);
        var pageSize = Math.Clamp(f.PageSize, 1, 200);

        var query = ApplyFilter(_db.Deliveries, f);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderBy(d => d.ScheduledDate).ThenBy(d => d.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListItem(_db)
            .ToListAsync(ct);

        return new PagedResult<DeliveryListItemDto>(items, total, page, pageSize);
    }

    /// <summary>Shared filtering used by list/board/summary queries.</summary>
    internal static IQueryable<Delivery> ApplyFilter(IQueryable<Delivery> query, DeliveryFilterDto f)
    {
        if (f.Date is { } date) query = query.Where(d => d.ScheduledDate == date);
        if (f.FromDate is { } from) query = query.Where(d => d.ScheduledDate >= from);
        if (f.ToDate is { } to) query = query.Where(d => d.ScheduledDate <= to);
        if (f.DeliveryBoyId is { } boyId) query = query.Where(d => d.DeliveryBoyId == boyId);
        if (f.AreaId is { } areaId) query = query.Where(d => d.Order != null && d.Order.AreaId == areaId);
        if (f.Status is not null && Enum.GetNames<DeliveryStatus>().Contains(f.Status))
        {
            var status = Enum.Parse<DeliveryStatus>(f.Status);
            query = query.Where(d => d.Status == status);
        }
        return query;
    }
}
