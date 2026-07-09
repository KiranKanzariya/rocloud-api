using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Roles.Dtos;

namespace ROCloud.Application.Features.Roles.Queries.GetPermissions;

/// <summary>All assignable permissions (system-wide lookup).</summary>
public sealed record GetPermissionsQuery : IRequest<List<PermissionDto>>;

public class GetPermissionsQueryHandler : IRequestHandler<GetPermissionsQuery, List<PermissionDto>>
{
    private readonly IAppDbContext _db;

    public GetPermissionsQueryHandler(IAppDbContext db) => _db = db;

    public async Task<List<PermissionDto>> Handle(GetPermissionsQuery request, CancellationToken ct)
    {
        return await _db.Permissions
            .OrderBy(p => p.Module).ThenBy(p => p.Action)
            .Select(p => new PermissionDto(p.Id, p.Module, p.Action, p.Code))
            .ToListAsync(ct);
    }
}
