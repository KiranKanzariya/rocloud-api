using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Security;
using ROCloud.Application.Common.Settings;

namespace ROCloud.Application.Features.Users;

/// <summary>
/// Issuing and withdrawing team invitations.
///
/// <para>An invitation is the only way a provisioned account becomes usable, so it is also the only
/// thing standing between a mistyped address and a stranger holding a role in someone's business.
/// Both halves matter: issuing one, and being able to take it back.</para>
/// </summary>
internal static class UserInvitations
{
    /// <summary>How long an invitation link stays valid.</summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromDays(7);

    /// <summary>
    /// The reverse lookup that makes revocation possible: user → the token currently outstanding for
    /// them. Without it, deactivating a member who never accepted would leave a live link in an inbox
    /// that could still turn their account on.
    /// </summary>
    private static string OutstandingKey(Guid userId) => $"invite:user:{userId}";

    /// <summary>
    /// Emails the member a link to set their password, and records the token so it can be withdrawn.
    /// The link points at the tenant's own portal and is consumed by the ordinary reset-password
    /// flow — one form for "set your first password" and "I forgot mine", not two.
    /// </summary>
    /// <param name="isReset">
    /// True when an owner is resetting an existing member rather than adding a new one. Only the
    /// wording differs — the mechanism is identical, because emailing a working password would put
    /// back exactly the standing credential this whole flow exists to remove.
    /// </param>
    public static async Task SendAsync(
        IAppDbContext db, ICacheService cache, IEmailService email, IAppSettings settings,
        Guid tenantId, Guid userId, string toEmail, CancellationToken ct, bool isReset = false)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await cache.SetAsync($"pwreset:{token}", new PasswordResetToken(userId), Ttl, ct);
        await cache.SetAsync(OutstandingKey(userId), new OutstandingInvite(token), Ttl, ct);

        var subdomain = await db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => t.Subdomain)
            .FirstAsync(ct);
        var url = $"{settings.TenantUrlFormat.Replace("{subdomain}", subdomain)}/reset-password?token={token}";

        await email.SendAsync(
            toEmail,
            isReset ? "Set a new ROCloud password" : "You've been invited to ROCloud",
            (isReset
                ? "Your ROCloud password has been reset by your business owner. "
                  + $"<a href=\"{url}\">Set a new password</a> to sign in again. "
                : "You've been invited to join your team on ROCloud. "
                  + $"<a href=\"{url}\">Accept your invitation</a> to set your password and activate your account. ")
            + $"This link is valid for {Ttl.Days} days.\n\n"
            // The wrong recipient of a mistyped invitation needs to know that ignoring it is enough.
            + "If you weren't expecting this, you can ignore this email — no account is active until the "
            + "link above is used.", ct);
    }

    /// <summary>
    /// Kills any outstanding invitation for this member. Called when they are deactivated or deleted:
    /// an unaccepted invitation is a pending grant of access, and switching someone off has to switch
    /// that off too, or the emailed link would quietly re-enable the account later.
    /// </summary>
    public static async Task RevokeAsync(ICacheService cache, Guid userId, CancellationToken ct)
    {
        var key = OutstandingKey(userId);
        if (await cache.GetAsync<OutstandingInvite>(key, ct) is { } outstanding)
            await cache.RemoveAsync($"pwreset:{outstanding.Token}", ct);
        await cache.RemoveAsync(key, ct);
    }

    /// <summary>Cache payload for the reverse lookup — a class because ICacheService requires one.</summary>
    private sealed record OutstandingInvite(string Token);
}
