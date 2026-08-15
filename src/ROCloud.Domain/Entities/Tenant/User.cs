using ROCloud.Domain.Entities.Common;
using ROCloud.Domain.Enums;

namespace ROCloud.Domain.Entities.Tenant;

/// <summary>A tenant team member (owner, manager, delivery boy, etc.). DB table: users.</summary>
public class User : BaseEntity, ITenantEntity, ILockableAccount
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

    /// <summary>
    /// When this member opened their invitation and set a password. NULL = invited, never accepted —
    /// the account exists but nobody has yet proved they own the address it was sent to, so it is not
    /// active and cannot be signed in to.
    ///
    /// <para>This is what makes a mistyped invitation harmless: it stays pending, visible to the owner
    /// as "Invited", instead of quietly becoming a working account for whoever received the email.</para>
    /// </summary>
    public DateTime? InviteAcceptedAt { get; set; }

    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// Access tokens issued before this moment are refused. NULL means nothing has been revoked.
    /// DB: users.sessions_valid_from.
    /// </summary>
    /// <remarks>
    /// The account-wide half of revocation. Ending the refresh chain
    /// (<c>UserSessions.RevokeAllAsync</c>) stops NEW access tokens being minted but does nothing about
    /// the ones already out there, which stay valid for the rest of their hour — so a password reset,
    /// a deactivation or a deletion used to take up to <c>Jwt:AccessTokenExpiryMinutes</c> to actually
    /// lock anybody out. Set this at the same time and the existing tokens die immediately.
    /// See <c>SessionValidityService</c>, which is what reads it.
    /// </remarks>
    public DateTime? SessionsValidFrom { get; set; }

    /// <summary>Consecutive failed sign-ins since the last success. DB: users.failed_login_attempts.</summary>
    public int FailedLoginAttempts { get; set; }

    /// <summary>
    /// Locked out until this moment; NULL or past means not locked. DB: users.lockout_ends_at.
    /// </summary>
    /// <remarks>
    /// On the row rather than in the cache because the cache is in-memory for v1 — a restart, or a
    /// second instance mid-deploy, used to clear every lockout in the system at once.
    /// See <c>LoginAttemptService</c>.
    /// </remarks>
    public DateTime? LockoutEndsAt { get; set; }

    // Navigation
    public Role? Role { get; set; }
    public ICollection<UserArea> AreaAssignments { get; set; } = new List<UserArea>();
}
