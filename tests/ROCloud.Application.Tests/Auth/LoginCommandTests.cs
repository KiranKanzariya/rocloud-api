using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Security;
using ROCloud.Application.Features.Auth.Commands.Login;
using ROCloud.Application.Features.Auth.Services;

namespace ROCloud.Application.Tests.Auth;

public class LoginCommandTests
{
    [Fact]
    public async Task LoginCommand_ValidCredentials_ReturnsTokens()
    {
        await using var db = AuthTestHelpers.NewDb();
        await AuthTestHelpers.SeedAsync(db);
        var attempts = new LoginAttemptService(AuthTestHelpers.NewCache(), new FakeAppSettings());
        var handler = new LoginCommandHandler(db, new Infrastructure.Identity.PasswordService(new ConfigurationBuilder().Build()), attempts,
            new AuthTokenIssuer(db, new FakeTokenService(), new FakeAppSettings()));

        var result = await handler.Handle(
            new LoginCommand(AuthTestHelpers.OwnerEmail, AuthTestHelpers.ValidPassword, AuthTestHelpers.Subdomain),
            CancellationToken.None);

        Assert.False(string.IsNullOrEmpty(result.AccessToken));
        Assert.False(string.IsNullOrEmpty(result.RefreshToken));

        var owner = await db.Users.IgnoreQueryFilters().FirstAsync();
        Assert.False(string.IsNullOrEmpty(owner.RefreshToken));
        Assert.NotNull(owner.RefreshTokenExpiresAt);
    }

    [Fact]
    public async Task LoginCommand_InvalidPassword_RecordsFailure()
    {
        await using var db = AuthTestHelpers.NewDb();
        await AuthTestHelpers.SeedAsync(db);
        var attempts = new LoginAttemptService(AuthTestHelpers.NewCache(), new FakeAppSettings());
        var handler = new LoginCommandHandler(db, new Infrastructure.Identity.PasswordService(new ConfigurationBuilder().Build()), attempts,
            new AuthTokenIssuer(db, new FakeTokenService(), new FakeAppSettings()));

        await Assert.ThrowsAsync<InvalidCredentialsException>(() => handler.Handle(
            new LoginCommand(AuthTestHelpers.OwnerEmail, "WrongPassword!1", AuthTestHelpers.Subdomain),
            CancellationToken.None));

        // The failure was recorded against this identifier.
        var clientId = $"{AuthTestHelpers.OwnerEmail}:{AuthTestHelpers.Subdomain}".ToLowerInvariant();
        var count = await attempts.RecordFailureAsync(clientId); // returns running total (now 2)
        Assert.Equal(2, count);

        // No session was issued.
        var owner = await db.Users.IgnoreQueryFilters().FirstAsync();
        Assert.True(string.IsNullOrEmpty(owner.RefreshToken));
    }

    [Fact]
    public async Task LoginCommand_5FailedAttempts_LocksAccount()
    {
        await using var db = AuthTestHelpers.NewDb();
        await AuthTestHelpers.SeedAsync(db);
        var attempts = new LoginAttemptService(AuthTestHelpers.NewCache(), new FakeAppSettings());
        var handler = new LoginCommandHandler(db, new Infrastructure.Identity.PasswordService(new ConfigurationBuilder().Build()), attempts,
            new AuthTokenIssuer(db, new FakeTokenService(), new FakeAppSettings()));

        var badLogin = new LoginCommand(AuthTestHelpers.OwnerEmail, "WrongPassword!1", AuthTestHelpers.Subdomain);

        for (var i = 0; i < new FakeAppSettings().MaxLoginAttempts; i++)
            await Assert.ThrowsAsync<InvalidCredentialsException>(() => handler.Handle(badLogin, CancellationToken.None));

        // The account is now locked — even the correct password is rejected with a lockout.
        await Assert.ThrowsAsync<AccountLockedException>(() => handler.Handle(
            new LoginCommand(AuthTestHelpers.OwnerEmail, AuthTestHelpers.ValidPassword, AuthTestHelpers.Subdomain),
            CancellationToken.None));
    }
}
