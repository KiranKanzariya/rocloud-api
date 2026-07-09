using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Platform.Users.Dtos;

namespace ROCloud.Application.Features.Platform.Users.Queries.GetPlatformUsers;

/// <summary>Lists all platform staff members.</summary>
public sealed record GetPlatformUsersQuery : IRequest<IReadOnlyList<PlatformUserDto>>;

public class GetPlatformUsersQueryHandler : IRequestHandler<GetPlatformUsersQuery, IReadOnlyList<PlatformUserDto>>
{
    private readonly IAppDbContext _db;

    public GetPlatformUsersQueryHandler(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<PlatformUserDto>> Handle(GetPlatformUsersQuery request, CancellationToken ct)
    {
        return await _db.PlatformUsers
            .OrderBy(u => u.Name)
            .Select(u => new PlatformUserDto(
                u.Id, u.Name, u.Email, u.PlatformRole, u.IsActive, u.LastLoginAt, u.CreatedAt))
            .ToListAsync(ct);
    }
}
