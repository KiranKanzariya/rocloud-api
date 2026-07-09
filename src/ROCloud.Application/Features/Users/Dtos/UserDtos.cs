namespace ROCloud.Application.Features.Users.Dtos;

/// <summary>A team member row for the users list.</summary>
public sealed record UserListItemDto(
    Guid Id,
    string Name,
    string? Mobile,
    string? Email,
    Guid? RoleId,
    string? RoleName,
    string AuthProvider,
    bool IsActive,
    DateTime? LastLoginAt,
    IReadOnlyList<AssignedAreaDto> Areas);

/// <summary>Full team member for the detail view.</summary>
public sealed record UserDto(
    Guid Id,
    string Name,
    string? Mobile,
    string? Email,
    Guid? RoleId,
    string? RoleName,
    string AuthProvider,
    string? PreferredLanguage,
    bool IsActive,
    DateTime? LastLoginAt,
    DateTime CreatedAt,
    IReadOnlyList<AssignedAreaDto> Areas);

public sealed record AssignedAreaDto(Guid AreaId, string AreaName);

/// <summary>Filter/paging for the users list.</summary>
public sealed record UserFilterDto
{
    public Guid? RoleId { get; init; }
    public bool? IsActive { get; init; }
    public string? Search { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}
