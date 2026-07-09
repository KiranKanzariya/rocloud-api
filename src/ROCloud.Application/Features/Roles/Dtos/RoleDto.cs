namespace ROCloud.Application.Features.Roles.Dtos;

public sealed record RoleDto(
    Guid Id,
    string Name,
    bool IsSystem,
    bool IsCustom,
    string[] Permissions);

public sealed record PermissionDto(
    Guid Id,
    string Module,
    string Action,
    string Code);
