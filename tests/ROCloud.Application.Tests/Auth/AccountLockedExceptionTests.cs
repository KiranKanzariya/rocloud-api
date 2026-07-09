using ROCloud.Application.Common.Exceptions;

namespace ROCloud.Application.Tests.Auth;

/// <summary>The lockout message reflects the configured duration (was hard-coded "15 minutes").</summary>
public class AccountLockedExceptionTests
{
    [Fact]
    public void Message_UsesConfiguredMinutes_WithPluralisation()
    {
        Assert.Contains("30 minutes", new AccountLockedException(30).Message);
        Assert.Contains("1 minute.", new AccountLockedException(1).Message);   // singular
    }
}
