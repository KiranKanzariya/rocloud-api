using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Features.Roles.Commands.DeleteRole;

/// <summary>Soft-deletes a custom role (system roles cannot be deleted; role must have no users).</summary>
public sealed record DeleteRoleCommand(Guid RoleId) : IRequest;

public class DeleteRoleCommandHandler : IRequestHandler<DeleteRoleCommand>
{
    private readonly IAppDbContext _db;

    public DeleteRoleCommandHandler(IAppDbContext db) => _db = db;

    public async Task Handle(DeleteRoleCommand request, CancellationToken ct)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == request.RoleId, ct)
                   ?? throw new NotFoundException("Role", request.RoleId);

        if (role.IsSystem || !role.IsCustom)
            throw new ForbiddenAccessException("System roles cannot be deleted.");

        var hasUsers = await _db.Users.AnyAsync(u => u.RoleId == role.Id, ct);
        if (hasUsers)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["role"] = ["Reassign all users to another role before deleting this one."]
            });

        role.IsDeleted = true;
        await _db.SaveChangesAsync(ct);
    }
}
