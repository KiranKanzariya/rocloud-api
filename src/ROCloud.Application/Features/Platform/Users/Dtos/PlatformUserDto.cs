namespace ROCloud.Application.Features.Platform.Users.Dtos;

/// <summary>A platform staff member (guide §26).</summary>
public sealed record PlatformUserDto(
    Guid Id,
    string Name,
    string Email,
    string PlatformRole,
    bool IsActive,
    DateTime? LastLoginAt,
    DateTime CreatedAt);
