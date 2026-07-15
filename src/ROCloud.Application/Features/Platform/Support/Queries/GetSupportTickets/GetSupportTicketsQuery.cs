using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Models;
using ROCloud.Application.Features.Platform.Support.Dtos;

namespace ROCloud.Application.Features.Platform.Support.Queries.GetSupportTickets;

/// <summary>Cross-tenant support tickets with filters and paging.</summary>
public sealed record GetSupportTicketsQuery(SupportFilterDto Filter) : IRequest<PagedResult<SupportTicketListItemDto>>;

public class GetSupportTicketsQueryHandler
    : IRequestHandler<GetSupportTicketsQuery, PagedResult<SupportTicketListItemDto>>
{
    private readonly IAppDbContext _db;

    public GetSupportTicketsQueryHandler(IAppDbContext db) => _db = db;

    public async Task<PagedResult<SupportTicketListItemDto>> Handle(GetSupportTicketsQuery request, CancellationToken ct)
    {
        var f = request.Filter;
        var query = _db.SupportTickets.AsQueryable();

        if (!string.IsNullOrWhiteSpace(f.Status)) query = query.Where(t => t.Status == f.Status);
        if (!string.IsNullOrWhiteSpace(f.Priority)) query = query.Where(t => t.Priority == f.Priority);
        if (f.TenantId is { } tid) query = query.Where(t => t.TenantId == tid);
        if (f.AssignedPlatformUserId is { } aid) query = query.Where(t => t.AssignedPlatformUserId == aid);

        var total = await query.CountAsync(ct);
        var page = Math.Max(1, f.Page);
        var size = Math.Clamp(f.PageSize, 1, 100);

        var rows = await query
            // Id tiebreaker keeps paging deterministic when tickets share a CreatedAt.
            .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
            .Skip((page - 1) * size).Take(size)
            .Select(t => new SupportTicketListItemDto(
                t.Id, t.TenantId, t.Tenant!.Name, t.Subject, t.Status, t.Priority,
                t.AssignedPlatformUserId, t.AssignedPlatformUser != null ? t.AssignedPlatformUser.Name : null,
                t.CreatedAt))
            .ToListAsync(ct);

        return new PagedResult<SupportTicketListItemDto>(rows, total, page, size);
    }
}
