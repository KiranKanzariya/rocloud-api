using ROCloud.Application.Common.Settings;
using ROCloud.Domain.Entities.Common;

namespace ROCloud.Application.Common.Security;

/// <summary>
/// Failed-login counting and lockout, held on the user row (Security:MaxLoginAttempts /
/// Security:LockoutMinutes, guide §10.2).
/// </summary>
/// <remarks>
/// <para>
/// This used to count in <c>ICacheService</c>, which is in-memory for v1 — so an API restart, or a
/// second instance during a zero-downtime deploy, handed an attacker a fresh five-attempt budget
/// against every account at once. A lockout that a restart clears is not a lockout; it is a delay.
/// Durable state belongs in the database, and the user row is already being read on the path that
/// needs it.
/// </para>
/// <para>
/// The counter is now per USER rather than per <c>email:subdomain</c> string, which means an address
/// that matches no account is no longer counted. Nothing is lost: there is no password to guess on an
/// account that does not exist, guessing addresses is what the IP rate limit on
/// <c>POST /api/auth/login</c> is for, and the response is identical either way, so this reveals
/// nothing about which addresses are real.
/// </para>
/// <para>
/// Nothing here calls SaveChanges — the caller owns the unit of work, as elsewhere in this namespace.
/// </para>
/// </remarks>
public class LoginAttemptService
{
    private readonly IAppSettings _settings;

    public LoginAttemptService(IAppSettings settings) => _settings = settings;

    /// <summary>Configured lockout duration in minutes — used to word the lockout message.</summary>
    public int LockoutMinutes => _settings.LockoutMinutes;

    /// <summary>True while this account is locked. Expiry is implicit: the stamp is simply in the past.</summary>
    public bool IsLockedOut(ILockableAccount user, DateTime? now = null)
        => user.LockoutEndsAt is { } until && until > (now ?? DateTime.UtcNow);

    /// <summary>
    /// Records one failed attempt, locking the account once the threshold is reached.
    /// </summary>
    /// <remarks>
    /// The counter resets as the lock is applied, so the NEXT lockout also takes a full
    /// <c>MaxLoginAttempts</c> failures rather than a single one — otherwise one lock would leave the
    /// account permanently one bad attempt away from the next.
    /// </remarks>
    public void RecordFailure(ILockableAccount user, DateTime? now = null)
    {
        var moment = now ?? DateTime.UtcNow;
        user.FailedLoginAttempts += 1;

        if (user.FailedLoginAttempts < _settings.MaxLoginAttempts) return;

        user.FailedLoginAttempts = 0;
        user.LockoutEndsAt = moment.AddMinutes(_settings.LockoutMinutes);
    }

    /// <summary>Clears the counter after a successful sign-in.</summary>
    public void Clear(ILockableAccount user)
    {
        user.FailedLoginAttempts = 0;
        user.LockoutEndsAt = null;
    }
}
