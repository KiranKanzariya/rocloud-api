using ROCloud.Domain.Entities.Common;

namespace ROCloud.Domain.Entities.Platform;

/// <summary>
/// A ROCloud platform staff member (super-admin portal). Not tenant-scoped.
/// DB table: platform_users. platform_role is one of SuperAdmin/Support/Finance.
/// </summary>
public class PlatformUser : BaseEntity, ILockableAccount
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

    /// <summary>
    /// Access tokens issued before this moment are refused. NULL means nothing has been revoked.
    /// DB: platform_users.sessions_valid_from.
    /// </summary>
    /// <remarks>
    /// Same reasoning as on the tenant User, and it matters more here: clearing the refresh token left
    /// an outstanding platform access token — which reaches every workspace — valid for the rest of
    /// its hour after a reset or a deactivation.
    /// </remarks>
    public DateTime? SessionsValidFrom { get; set; }

    /// <summary>Consecutive failed sign-ins since the last success. DB: failed_login_attempts.</summary>
    public int FailedLoginAttempts { get; set; }

    /// <summary>
    /// Locked out until this moment; NULL or past means not locked. DB: lockout_ends_at.
    /// </summary>
    /// <remarks>
    /// On the row rather than in the in-memory cache, which a restart cleared. This account type
    /// reaches every workspace on the platform, so its lockout surviving a deploy matters more than
    /// any single tenant's.
    /// </remarks>
    public DateTime? LockoutEndsAt { get; set; }
}
