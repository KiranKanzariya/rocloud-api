namespace ROCloud.Domain.Entities.Common;

/// <summary>
/// An account that can be locked out after repeated failed sign-ins.
/// </summary>
/// <remarks>
/// Implemented by both <c>User</c> and <c>PlatformUser</c> so the lockout rules live in one place
/// (<c>LoginAttemptService</c>) rather than being written twice. The super-admin sign-in matters at
/// least as much as a tenant's — a platform account reaches every workspace — and it previously shared
/// the same in-memory counter, which a restart cleared.
/// </remarks>
public interface ILockableAccount
{
    /// <summary>Consecutive failed sign-ins since the last success.</summary>
    int FailedLoginAttempts { get; set; }

    /// <summary>Locked out until this moment. NULL or past means not locked.</summary>
    DateTime? LockoutEndsAt { get; set; }
}
