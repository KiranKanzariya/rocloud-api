using ROCloud.Domain.Entities.Common;

namespace ROCloud.Domain.Entities.Tenant;

/// <summary>
/// One signed-in device. Replaces the single <c>users.refresh_token</c> slot, which allowed a user
/// exactly one live session: signing in on the phone overwrote the portal's token, and the portal's
/// next refresh then presented a token that no longer matched — which the replay check treated as
/// theft and answered by clearing the slot, taking the phone's session down with it. One login
/// logged both devices out, up to an access-token lifetime later.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SessionId"/> is the device; <see cref="TokenHash"/> is the current token for it.
/// Refreshing rotates the hash — the old row is revoked and a new one written with the SAME
/// SessionId — so a replayed token can still be recognised and answered by revoking that device's
/// chain, and only that one.
/// </para>
/// <para>
/// Deliberately NOT an <c>ITenantEntity</c>: that interface attaches a global query filter keyed on
/// the resolved tenant, and every path that touches this table (refresh, logout) runs on routes
/// TenantMiddleware excludes, where there is no tenant to resolve. <see cref="TenantId"/> is still
/// stored, for the foreign key and for cleanup when a tenant is deleted.
/// </para>
/// </remarks>
public class UserSession : BaseEntity
{
    public Guid TenantId { get; set; }

    public Guid UserId { get; set; }

    /// <summary>Stable across rotations: one value per device, for the life of that sign-in.</summary>
    public Guid SessionId { get; set; }

    /// <summary>SHA-256 of the current refresh token — never the raw token.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Human-readable device name, e.g. "Pixel 7 · Android" or "Chrome on Windows". DB: label.
    /// </summary>
    /// <remarks>
    /// Without it the sessions list is a column of UUIDs, and an owner asked "is one of these not
    /// yours?" has no way to answer. That matters most in the case this table exists to make safe: a
    /// second copy of the app — an OEM dual-app clone, a work profile, a handset someone else had for
    /// ten minutes — is a perfectly ordinary second session, indistinguishable from a legitimate one
    /// except by what device it says it is.
    /// </remarks>
    public string? Label { get; set; }

    /// <summary>
    /// When this session last exchanged its refresh token. DB: last_seen_at.
    /// </summary>
    /// <remarks>
    /// Updated on rotation rather than per request — roughly hourly, which is precise enough to tell
    /// "in use today" from "not since March" and costs no extra write.
    /// </remarks>
    public DateTime? LastSeenAt { get; set; }

    /// <summary>Null while the row is the live token for its session. Set on rotation, on sign-out,
    /// and when the whole chain is revoked.</summary>
    public DateTime? RevokedAt { get; set; }

    public bool IsLive(DateTime now) => RevokedAt is null && ExpiresAt > now;
}
