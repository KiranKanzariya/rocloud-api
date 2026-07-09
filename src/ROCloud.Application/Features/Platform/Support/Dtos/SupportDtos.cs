namespace ROCloud.Application.Features.Platform.Support.Dtos;

/// <summary>A support ticket row (guide §26).</summary>
public sealed record SupportTicketListItemDto(
    Guid Id,
    Guid TenantId,
    string TenantName,
    string Subject,
    string Status,
    string Priority,
    Guid? AssignedPlatformUserId,
    string? AssignedPlatformUserName,
    DateTime CreatedAt);

/// <summary>Full support ticket for the detail view.</summary>
public sealed record SupportTicketDto(
    Guid Id,
    Guid TenantId,
    string TenantName,
    string Subject,
    string? Description,
    string Status,
    string Priority,
    Guid? AssignedPlatformUserId,
    string? AssignedPlatformUserName,
    string? ResolutionNote,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

/// <summary>Filter/paging for the support list.</summary>
public sealed record SupportFilterDto
{
    public string? Status { get; init; }
    public string? Priority { get; init; }
    public Guid? TenantId { get; init; }
    public Guid? AssignedPlatformUserId { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}
