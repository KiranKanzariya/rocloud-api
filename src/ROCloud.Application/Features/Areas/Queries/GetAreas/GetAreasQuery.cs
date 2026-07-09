using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Areas.Dtos;

namespace ROCloud.Application.Features.Areas.Queries.GetAreas;

/// <summary>Lists the tenant's delivery areas. By default active only.</summary>
public sealed record GetAreasQuery(bool IncludeInactive = false) : IRequest<IReadOnlyList<AreaDto>>;

public class GetAreasQueryHandler : IRequestHandler<GetAreasQuery, IReadOnlyList<AreaDto>>
{
    private readonly IAppDbContext _db;

    public GetAreasQueryHandler(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<AreaDto>> Handle(GetAreasQuery request, CancellationToken ct)
    {
        var query = _db.Areas.AsQueryable();
        if (!request.IncludeInactive)
            query = query.Where(a => a.IsActive);

        return await query
            .OrderBy(a => a.Name)
            .Select(a => new AreaDto(
                a.Id, a.Name, a.City, a.Pincode, a.IsActive,
                a.Customers.Count(c => !c.IsDeleted)))
            .ToListAsync(ct);
    }
}
