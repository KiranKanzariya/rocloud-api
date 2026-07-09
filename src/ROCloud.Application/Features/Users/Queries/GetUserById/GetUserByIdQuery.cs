using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Users.Dtos;

namespace ROCloud.Application.Features.Users.Queries.GetUserById;

public sealed record GetUserByIdQuery(Guid Id) : IRequest<UserDto>;

public class GetUserByIdQueryHandler : IRequestHandler<GetUserByIdQuery, UserDto>
{
    private readonly IAppDbContext _db;

    public GetUserByIdQueryHandler(IAppDbContext db) => _db = db;

    public async Task<UserDto> Handle(GetUserByIdQuery request, CancellationToken ct)
    {
        var user = await _db.Users
            .Include(u => u.Role)
            .Include(u => u.AreaAssignments).ThenInclude(ua => ua.Area)
            .FirstOrDefaultAsync(u => u.Id == request.Id, ct)
            ?? throw new NotFoundException("User", request.Id);

        var areas = user.AreaAssignments
            .Where(ua => ua.Area != null)
            .Select(ua => new AssignedAreaDto(ua.AreaId, ua.Area!.Name))
            .ToList();

        return new UserDto(
            user.Id, user.Name, user.Mobile, user.Email,
            user.RoleId, user.Role?.Name,
            user.AuthProvider.ToString().ToLower(),
            user.PreferredLanguage, user.IsActive, user.LastLoginAt, user.CreatedAt, areas);
    }
}
