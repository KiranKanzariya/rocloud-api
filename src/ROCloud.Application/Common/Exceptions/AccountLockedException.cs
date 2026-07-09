namespace ROCloud.Application.Common.Exceptions;

/// <summary>Thrown when an account is temporarily locked after too many failed logins (guide §10.2).</summary>
public class AccountLockedException : Exception
{
    public AccountLockedException(int lockoutMinutes)
        : base($"Too many failed attempts. Try again in {lockoutMinutes} minute{(lockoutMinutes == 1 ? "" : "s")}.") { }
}
