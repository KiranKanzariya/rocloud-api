using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Enums;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Features.Users.Commands.DeleteUser;

/// <summary>
/// Soft-deletes a team member (is_deleted=true). The row stays for history — orders, payments and
/// audit entries keep pointing at it — but the user disappears from every list and can no longer sign
/// in. The partial unique index on (tenant_id, email) is scoped to is_deleted=false, so the address is
/// freed and the person can be re-invited later.
/// Deactivate is the reversible action; this one is not offered for a user who still has work assigned.
/// </summary>
public sealed record DeleteUserCommand(Guid Id) : IRequest;

public class DeleteUserCommandHandler : IRequestHandler<DeleteUserCommand>
{
    private const string OwnerRole = "Owner";

    private static readonly DeliveryStatus[] OpenStatuses = [DeliveryStatus.Pending, DeliveryStatus.InTransit];

    private readonly IAppDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public DeleteUserCommandHandler(IAppDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task Handle(DeleteUserCommand request, CancellationToken ct)
    {
        var user = await _db.Users
            .Include(u => u.Role)
            .Include(u => u.AreaAssignments)
            .FirstOrDefaultAsync(u => u.Id == request.Id, ct)
            ?? throw new NotFoundException("User", request.Id);

        // Deleting yourself would end your own session mid-request and, for a sole owner, orphan the
        // whole workspace. Remove another owner's account instead, or have them do it.
        if (user.Id == _currentUser.UserId)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["user"] = ["You cannot delete your own account."]
            });

        // Never remove the last active Owner — nobody would be left holding *.Manage. Mirrors DeactivateUser.
        if (user.IsActive && user.Role?.Name == OwnerRole)
        {
            var otherActiveOwners = await _db.Users.CountAsync(
                u => u.Id != user.Id && u.IsActive && u.Role != null && u.Role.Name == OwnerRole, ct);
            if (otherActiveOwners == 0)
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["user"] = ["You cannot delete the last active owner."]
                });
        }

        // A delivery boy with stops still on the board would leave those deliveries pointing at a row no
        // query can see, so the route would render a blank name. Reassign or finish them first.
        var openDeliveries = await _db.Deliveries
            .CountAsync(d => d.DeliveryBoyId == user.Id && OpenStatuses.Contains(d.Status), ct);
        if (openDeliveries > 0)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["user"] = [$"{user.Name} still has {openDeliveries} pending deliveries. Complete or reassign them first."]
            });

        // Area assignments are a live routing input (DeliveryBoyResolver picks the boy mapped to the
        // customer's area) and the table has no soft-delete column — drop the rows outright.
        _db.UserAreas.RemoveRange(user.AreaAssignments);

        user.IsDeleted = true;
        user.IsActive = false;          // any surviving access token stops being honoured on refresh
        user.RefreshToken = null;
        user.RefreshTokenExpiresAt = null;
        await _db.SaveChangesAsync(ct);
    }
}
