using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Models;
using ROCloud.Application.Features.ServiceRequests.Dtos;
using ROCloud.Application.Features.ServiceRequests.Queries;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.ServiceRequests.Queries.GetServiceRequests;

public sealed record GetServiceRequestsQuery(ServiceRequestFilterDto Filter)
    : IRequest<PagedResult<ServiceRequestListItemDto>>;

public class GetServiceRequestsQueryHandler
    : IRequestHandler<GetServiceRequestsQuery, PagedResult<ServiceRequestListItemDto>>
{
    private readonly IAppDbContext _db;

    public GetServiceRequestsQueryHandler(IAppDbContext db) => _db = db;

    public async Task<PagedResult<ServiceRequestListItemDto>> Handle(
        GetServiceRequestsQuery request, CancellationToken ct)
    {
        var f = request.Filter;
        var page = Math.Max(1, f.Page);
        var pageSize = Math.Clamp(f.PageSize, 1, 100);

        IQueryable<ServiceRequest> query = _db.ServiceRequests;

        if (f.CustomerId is { } customerId) query = query.Where(s => s.CustomerId == customerId);
        if (f.AssignedTechId is { } techId) query = query.Where(s => s.AssignedTechId == techId);
        if (f.Status is not null && Enum.GetNames<ServiceRequestStatus>().Contains(f.Status))
        {
            var status = Enum.Parse<ServiceRequestStatus>(f.Status);
            query = query.Where(s => s.Status == status);
        }
        if (f.Priority is not null && Enum.GetNames<ServicePriority>().Contains(f.Priority))
        {
            var priority = Enum.Parse<ServicePriority>(f.Priority);
            query = query.Where(s => s.Priority == priority);
        }
        if (f.ServiceType is not null && Enum.GetNames<ServiceType>().Contains(f.ServiceType))
        {
            var type = Enum.Parse<ServiceType>(f.ServiceType);
            query = query.Where(s => s.ServiceType == type);
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.TicketNumber)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListItem(_db)
            .ToListAsync(ct);

        return new PagedResult<ServiceRequestListItemDto>(items, total, page, pageSize);
    }
}
