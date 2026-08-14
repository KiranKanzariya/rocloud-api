using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;

namespace ROCloud.Application.Common.Security;

/// <summary>
/// Revoking signed-in devices, in the two shapes the app needs.
/// </summary>
/// <remarks>
/// The distinction is the whole point of the sessions table, so it is worth stating plainly:
/// <list type="bullet">
/// <item><see cref="RevokeChainAsync"/> ends ONE device. Signing out on the phone, or replaying a
/// token that belonged to it, must not touch the owner's portal session.</item>
/// <item><see cref="RevokeAllAsync"/> ends every device, and is for the cases where the account
/// itself changed hands or was shut down — a password reset, a deactivation, a deletion. There,
/// leaving another device signed in is the bug.</item>
/// </list>
/// Rows are marked rather than deleted so a replayed token is still recognisable as one that USED to
/// be valid, instead of looking like a random string. <see cref="Common.Interfaces.IAppDbContext"/>
/// callers save; nothing here calls SaveChanges, so a revoke can join the caller's transaction.
/// </remarks>
public static class UserSessions
{
    /// <summary>Ends every live session for a user.</summary>
    public static async Task RevokeAllAsync(IAppDbContext db, Guid userId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var live = await db.UserSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var session in live)
            session.RevokedAt = now;
    }

    /// <summary>
    /// Ends one device: every row in a rotation chain, live or not. Used on sign-out, and on a
    /// replayed token — a token that was rotated away and then presented again is either a stale
    /// retry or a stolen copy, and neither should keep that device signed in.
    /// </summary>
    public static async Task RevokeChainAsync(IAppDbContext db, Guid sessionId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var chain = await db.UserSessions
            .Where(s => s.SessionId == sessionId && s.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var session in chain)
            session.RevokedAt = now;
    }
}
