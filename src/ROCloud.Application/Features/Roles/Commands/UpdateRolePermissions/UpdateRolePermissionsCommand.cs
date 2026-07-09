using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Subscription;
using ROCloud.Domain.Entities.Tenant;

namespace ROCloud.Application.Features.Roles.Commands.UpdateRolePermissions;

/// <summary>Replaces a role's permission set. The Owner role is immutable.</summary>
public sealed record UpdateRolePermissionsCommand(Guid RoleId, string[] PermissionCodes) : IRequest;

public class UpdateRolePermissionsCommandHandler : IRequestHandler<UpdateRolePermissionsCommand>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;

    public UpdateRolePermissionsCommandHandler(IAppDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task Handle(UpdateRolePermissionsCommand request, CancellationToken ct)
    {
        var role = await _db.Roles
            .Include(r => r.RolePermissions)
            .FirstOrDefaultAsync(r => r.Id == request.RoleId, ct)
            ?? throw new NotFoundException("Role", request.RoleId);

        // The Owner role must always retain every permission — it cannot be changed.
        if (role.IsSystem && role.Name == "Owner")
            throw new ForbiddenAccessException("The Owner role's permissions cannot be changed.");

        // Editing a custom role's permissions is a paid capability; system roles stay editable regardless.
        if (role.IsCustom)
            await PlanFeatures.EnsureCustomRolesAsync(_db, _tenant, ct);

        var desired = await _db.Permissions
            .Where(p => request.PermissionCodes.Contains(p.Code))
            .ToListAsync(ct);
        var desiredIds = desired.Select(p => p.Id).ToHashSet();
        var existingIds = role.RolePermissions.Select(rp => rp.PermissionId).ToHashSet();

        var toRemove = role.RolePermissions.Where(rp => !desiredIds.Contains(rp.PermissionId)).ToList();
        var toAdd = desired.Where(p => !existingIds.Contains(p.Id)).ToList();

        _db.RolePermissions.RemoveRange(toRemove);
        foreach (var permission in toAdd)
            _db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id });

        await _db.SaveChangesAsync(ct);
    }
}
