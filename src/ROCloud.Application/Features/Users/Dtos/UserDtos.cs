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
    /// <summary>Filter by role NAME — role ids are per-tenant, so a caller wanting "the technicians"
    /// cannot know the id up front (the technician dropdown needs exactly this).</summary>
    public string? RoleName { get; init; }
    public bool? IsActive { get; init; }
    public string? Search { get; init; }
    public string? SortBy { get; init; }
    public string? SortDir { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}
