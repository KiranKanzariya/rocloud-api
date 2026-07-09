using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Roles.Dtos;

namespace ROCloud.Application.Features.Roles.Queries.GetRoles;

/// <summary>All roles for the current tenant with their granted permission codes.</summary>
public sealed record GetRolesQuery : IRequest<List<RoleDto>>;

public class GetRolesQueryHandler : IRequestHandler<GetRolesQuery, List<RoleDto>>
{
    private readonly IAppDbContext _db;

    public GetRolesQueryHandler(IAppDbContext db) => _db = db;

    public async Task<List<RoleDto>> Handle(GetRolesQuery request, CancellationToken ct)
    {
        var roles = await _db.Roles
            .Include(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
            .OrderBy(r => r.Name)
            .ToListAsync(ct);

        return roles.Select(r => new RoleDto(
            r.Id, r.Name, r.IsSystem, r.IsCustom,
            r.RolePermissions
                .Where(rp => rp.Permission != null)
                .Select(rp => rp.Permission!.Code)
                .OrderBy(c => c)
                .ToArray()))
            .ToList();
    }
}
