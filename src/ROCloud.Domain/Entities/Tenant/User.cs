using ROCloud.Domain.Entities.Common;
using ROCloud.Domain.Enums;

namespace ROCloud.Domain.Entities.Tenant;

/// <summary>A tenant team member (owner, manager, delivery boy, etc.). DB table: users.</summary>
public class User : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid? RoleId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Mobile { get; set; }
    public string? Email { get; set; }
    public string? PasswordHash { get; set; }
    public string? GoogleId { get; set; }
    public string? GoogleEmail { get; set; }
    public string? AvatarUrl { get; set; }
    public AuthProvider AuthProvider { get; set; } = AuthProvider.Custom;

    /// <summary>SHA-256 hash of the current refresh token (never the raw token). DB: refresh_token.</summary>
    public string? RefreshToken { get; set; }

    /// <summary>Expiry of the current refresh token. DB: refresh_token_expires_at (added Phase 5).</summary>
    public DateTime? RefreshTokenExpiresAt { get; set; }

    public string? DeviceToken { get; set; }

    /// <summary>Per-user language override (§4c.3). DB: users.preferred_language.</summary>
    public string? PreferredLanguage { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }

    // Navigation
    public Role? Role { get; set; }
    public ICollection<UserArea> AreaAssignments { get; set; } = new List<UserArea>();
}
