using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Models;
using ROCloud.Application.Features.Platform.Tenants.Dtos;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Platform.Tenants.Queries.GetTenants;

/// <summary>Lists all tenants across the platform with filters and paging.</summary>
public sealed record GetTenantsQuery(TenantFilterDto Filter) : IRequest<PagedResult<TenantListItemDto>>;

public class GetTenantsQueryHandler : IRequestHandler<GetTenantsQuery, PagedResult<TenantListItemDto>>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;

    public GetTenantsQueryHandler(IAppDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<PagedResult<TenantListItemDto>> Handle(GetTenantsQuery request, CancellationToken ct)
    {
        var f = request.Filter;
        var query = _db.Tenants.Include(t => t.Plan).Where(t => !t.IsDeleted);

        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.Trim().ToLower();
            query = query.Where(t => t.Name.ToLower().Contains(s)
                                     || t.Subdomain.ToLower().Contains(s)
                                     || t.OwnerEmail.ToLower().Contains(s));
        }
        if (!string.IsNullOrWhiteSpace(f.Status) && Enum.TryParse<TenantStatus>(f.Status, out var status))
            query = query.Where(t => t.Status == status);
        if (!string.IsNullOrWhiteSpace(f.PlanType) && Enum.TryParse<PlanType>(f.PlanType, out var plan))
            query = query.Where(t => t.Plan!.PlanType == plan);

        var total = await query.CountAsync(ct);
        var page = Math.Max(1, f.Page);
        var size = Math.Clamp(f.PageSize, 1, 100);

        var rows = await query
            // Id tiebreaker keeps paging deterministic when tenants share a CreatedAt.
            .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
            .Skip((page - 1) * size).Take(size)
            .Select(t => new
            {
                t.Id, t.Name, t.Subdomain, PlanName = t.Plan!.Name, PlanType = t.Plan.PlanType,
                t.Status, t.OwnerName, t.OwnerEmail, t.CreatedAt
            })
            .ToListAsync(ct);

        // customers is RLS-protected: IgnoreQueryFilters bypasses the EF global filter but NOT the
        // Postgres row-level-security policy, so a cross-tenant batch count returns 0 for platform
        // requests (no tenant context). Scope the connection to each tenant — as GetTenantById does —
        // and count individually. The page size is bounded (≤100), so this stays a small fan-out.
        // Restore the ambient context afterwards. ITenantContext is request-scoped, so leaving it set
        // to the LAST tenant on the page would silently re-scope anything that ran later in the same
        // request — a platform request must not end up impersonating an arbitrary tenant.
        var originalTenantId = _tenant.TenantId;
        var counts = new Dictionary<Guid, int>();
        try
        {
            foreach (var r in rows)
            {
                _tenant.TenantId = r.Id;
                counts[r.Id] = await _db.Customers.CountAsync(c => c.TenantId == r.Id && !c.IsDeleted, ct);
            }
        }
        finally
        {
            _tenant.TenantId = originalTenantId;
        }

        var items = rows.Select(r => new TenantListItemDto(
            r.Id, r.Name, r.Subdomain, r.PlanName, r.PlanType.ToString(), r.Status.ToString(),
            r.OwnerName, r.OwnerEmail, counts.GetValueOrDefault(r.Id, 0), r.CreatedAt)).ToList();

        return new PagedResult<TenantListItemDto>(items, total, page, size);
    }
}
