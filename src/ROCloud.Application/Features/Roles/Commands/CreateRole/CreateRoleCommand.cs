using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Subscription;
using ROCloud.Domain.Entities.Tenant;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Features.Roles.Commands.CreateRole;

/// <summary>Creates a custom role. Requires the plan's CustomRolesEnabled flag (enforced in the handler).</summary>
public sealed record CreateRoleCommand(string Name, string[] PermissionCodes) : IRequest<Guid>;

public class CreateRoleCommandValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().Length(2, 100);
        RuleFor(x => x.PermissionCodes).NotNull();
    }
}

public class CreateRoleCommandHandler : IRequestHandler<CreateRoleCommand, Guid>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;

    public CreateRoleCommandHandler(IAppDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<Guid> Handle(CreateRoleCommand request, CancellationToken ct)
    {
        await PlanFeatures.EnsureCustomRolesAsync(_db, _tenant, ct);

        var nameTaken = await _db.Roles.AnyAsync(r => r.Name == request.Name, ct);
        if (nameTaken)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["name"] = ["A role with this name already exists."]
            });

        var permissions = await _db.Permissions
            .Where(p => request.PermissionCodes.Contains(p.Code))
            .ToListAsync(ct);

        var role = new Role
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            Name = request.Name,
            IsSystem = false,
            IsCustom = true
        };
        _db.Roles.Add(role);

        foreach (var permission in permissions)
            _db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id });

        await _db.SaveChangesAsync(ct);
        return role.Id;
    }
}
