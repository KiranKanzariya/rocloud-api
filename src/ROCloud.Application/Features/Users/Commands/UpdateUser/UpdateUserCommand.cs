using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Entities.Tenant;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Features.Users.Commands.UpdateUser;

/// <summary>Updates a team member's name, mobile, role, area assignments and active status.</summary>
public sealed record UpdateUserCommand(
    Guid Id,
    string Name,
    string? Mobile,
    Guid RoleId,
    bool IsActive,
    IReadOnlyList<Guid>? AreaIds) : IRequest;

public class UpdateUserCommandValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserCommandValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Name).NotEmpty().Length(2, 200);
        RuleFor(c => c.Mobile)
            .Matches(@"^\+91[0-9]{10}$").When(c => !string.IsNullOrEmpty(c.Mobile))
            .WithMessage("Invalid mobile number.");
        RuleFor(c => c.RoleId).NotEmpty();
    }
}

public class UpdateUserCommandHandler : IRequestHandler<UpdateUserCommand>
{
    private const string OwnerRole = "Owner";

    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;

    public UpdateUserCommandHandler(IAppDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task Handle(UpdateUserCommand request, CancellationToken ct)
    {
        var user = await _db.Users
            .Include(u => u.AreaAssignments)
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == request.Id, ct)
            ?? throw new NotFoundException("User", request.Id);

        var targetRole = await _db.Roles.FirstOrDefaultAsync(r => r.Id == request.RoleId, ct)
            ?? throw new ValidationException(new Dictionary<string, string[]>
            {
                ["roleId"] = ["The selected role does not exist."]
            });

        // Never let this edit remove the tenant's last active Owner — demoting their role or
        // deactivating them would leave nobody able to manage users/roles/settings (only the Owner
        // role holds *.Manage). Mirrors the guard in DeactivateUser, which this path could bypass.
        var wasActiveOwner = user.IsActive && user.Role?.Name == OwnerRole;
        var willBeActiveOwner = request.IsActive && targetRole.Name == OwnerRole;
        if (wasActiveOwner && !willBeActiveOwner)
        {
            var otherActiveOwners = await _db.Users.CountAsync(
                u => u.Id != user.Id && u.IsActive && u.Role != null && u.Role.Name == OwnerRole, ct);
            if (otherActiveOwners == 0)
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["user"] = ["You cannot remove the last active owner. Assign the Owner role to another active user first."]
                });
        }

        // Promoting/reactivating someone into an active delivery-boy must respect the plan cap.
        var wasActiveDeliveryBoy = user.IsActive && user.Role?.Name == "DeliveryBoy";
        var willBeActiveDeliveryBoy = request.IsActive && targetRole.Name == "DeliveryBoy";
        if (willBeActiveDeliveryBoy && !wasActiveDeliveryBoy)
            await Subscription.PlanLimits.EnsureCanAddDeliveryBoyAsync(_db, _tenant, ct);

        await UserProvisioning.ValidateAreasAsync(_db, request.AreaIds, ct);

        user.Name = request.Name;
        user.Mobile = request.Mobile;
        user.RoleId = request.RoleId;
        user.IsActive = request.IsActive;

        // Replace the whole area set.
        _db.UserAreas.RemoveRange(user.AreaAssignments);
        if (request.AreaIds is { Count: > 0 })
            foreach (var areaId in request.AreaIds.Distinct())
                _db.UserAreas.Add(new UserArea
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenant.TenantId,
                    UserId = user.Id,
                    AreaId = areaId
                });

        await _db.SaveChangesAsync(ct);
    }
}
