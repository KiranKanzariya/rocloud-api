using ROCloud.Domain.Entities.Common;

namespace ROCloud.Domain.Entities.Platform;

/// <summary>
/// A ROCloud platform staff member (super-admin portal). Not tenant-scoped.
/// DB table: platform_users. platform_role is one of SuperAdmin/Support/Finance.
/// </summary>
public class PlatformUser : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public string PlatformRole { get; set; } = "Support";
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }

    /// <summary>SHA-256 hash of the current refresh token (rotation; guide §26).</summary>
    public string? RefreshToken { get; set; }
    public DateTime? RefreshTokenExpiresAt { get; set; }
}
