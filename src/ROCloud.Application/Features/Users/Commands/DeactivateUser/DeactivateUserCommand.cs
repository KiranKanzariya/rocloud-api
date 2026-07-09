using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Features.Users.Commands.DeactivateUser;

/// <summary>
/// Deactivates a team member (is_active=false). This is a soft action — the row and its audit
/// history are preserved. Refresh tokens are revoked so existing sessions can't continue.
/// </summary>
public sealed record DeactivateUserCommand(Guid Id) : IRequest;

public class DeactivateUserCommandHandler : IRequestHandler<DeactivateUserCommand>
{
    private const string OwnerRole = "Owner";

    private readonly IAppDbContext _db;

    public DeactivateUserCommandHandler(IAppDbContext db) => _db = db;

    public async Task Handle(DeactivateUserCommand request, CancellationToken ct)
    {
        var user = await _db.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == request.Id, ct)
            ?? throw new NotFoundException("User", request.Id);

        // Never deactivate the last active Owner — that would lock the tenant out.
        if (user.IsActive && user.Role?.Name == OwnerRole)
        {
            var otherActiveOwners = await _db.Users.CountAsync(
                u => u.Id != user.Id && u.IsActive && u.Role != null && u.Role.Name == OwnerRole, ct);
            if (otherActiveOwners == 0)
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["user"] = ["You cannot deactivate the last active owner."]
                });
        }

        user.IsActive = false;
        user.RefreshToken = null;
        user.RefreshTokenExpiresAt = null;
        await _db.SaveChangesAsync(ct);
    }
}
