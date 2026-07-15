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
        if (!string.IsNullOrWhiteSpace(f.RoleName))
        {
            var roleName = f.RoleName.Trim();
            query = query.Where(u => u.Role != null && u.Role.Name == roleName);
        }
        if (f.IsActive is { } isActive) query = query.Where(u => u.IsActive == isActive);
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            // Trim: a pasted or autocompleted "ramesh " would otherwise match nothing at all.
            var s = f.Search.Trim().ToLower();
            query = query.Where(u =>
                u.Name.ToLower().Contains(s) ||
                (u.Email != null && u.Email.ToLower().Contains(s)) ||
                (u.Mobile != null && u.Mobile.Contains(s)));
        }

        var total = await query.CountAsync(ct);

        var descending = string.Equals(f.SortDir, "desc", StringComparison.OrdinalIgnoreCase);
        IOrderedQueryable<User> ordered = (f.SortBy?.ToLowerInvariant()) switch
        {
            "email" => descending ? query.OrderByDescending(u => u.Email) : query.OrderBy(u => u.Email),
            "rolename" => descending
                ? query.OrderByDescending(u => u.Role!.Name)
                : query.OrderBy(u => u.Role!.Name),
            "lastloginat" => descending
                ? query.OrderByDescending(u => u.LastLoginAt)
                : query.OrderBy(u => u.LastLoginAt),
            "isactive" => descending
                ? query.OrderByDescending(u => u.IsActive)
                : query.OrderBy(u => u.IsActive),
            _ => descending ? query.OrderByDescending(u => u.Name) : query.OrderBy(u => u.Name)
        };
        // Roles, statuses and even names repeat, and LastLoginAt is null for anyone who's never logged
        // in — all big ties. A unique final key keeps the order stable and pagination correct.
        query = ordered.ThenBy(u => u.Id);

        var items = await query
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
