using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Models;
using ROCloud.Application.Features.Users.Dtos;
using ROCloud.Domain.Entities.Tenant;

namespace ROCloud.Application.Features.Users.Queries.GetUsers;

public sealed record GetUsersQuery(UserFilterDto Filter) : IRequest<PagedResult<UserListItemDto>>;

public class GetUsersQueryHandler : IRequestHandler<GetUsersQuery, PagedResult<UserListItemDto>>
{
    private readonly IAppDbContext _db;

    public GetUsersQueryHandler(IAppDbContext db) => _db = db;

    public async Task<PagedResult<UserListItemDto>> Handle(GetUsersQuery request, CancellationToken ct)
    {
        var f = request.Filter;
        var page = Math.Max(1, f.Page);
        var pageSize = Math.Clamp(f.PageSize, 1, 100);

        IQueryable<User> query = _db.Users;

        if (f.RoleId is { } roleId) query = query.Where(u => u.RoleId == roleId);
        if (f.IsActive is { } isActive) query = query.Where(u => u.IsActive == isActive);
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.ToLower();
            query = query.Where(u =>
                u.Name.ToLower().Contains(s) ||
                (u.Email != null && u.Email.ToLower().Contains(s)) ||
                (u.Mobile != null && u.Mobile.Contains(s)));
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderBy(u => u.Name)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(u => new UserListItemDto(
                u.Id,
                u.Name,
                u.Mobile,
                u.Email,
                u.RoleId,
                u.Role != null ? u.Role.Name : null,
                u.AuthProvider.ToString().ToLower(),
                u.IsActive,
                u.LastLoginAt,
                u.AreaAssignments
                    .Where(ua => ua.Area != null)
                    .Select(ua => new AssignedAreaDto(ua.AreaId, ua.Area!.Name))
                    .ToList()))
            .ToListAsync(ct);

        return new PagedResult<UserListItemDto>(items, total, page, pageSize);
    }
}
