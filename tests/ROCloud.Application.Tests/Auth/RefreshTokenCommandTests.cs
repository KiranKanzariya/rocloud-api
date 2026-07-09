using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Features.Auth.Commands.RefreshToken;
using ROCloud.Application.Features.Auth.Services;

namespace ROCloud.Application.Tests.Auth;

public class RefreshTokenCommandTests
{
    [Fact]
    public async Task RefreshToken_OldToken_FailsAndRevokesAllSessions()
    {
        await using var db = AuthTestHelpers.NewDb();
        var (tenant, owner) = await AuthTestHelpers.SeedAsync(db);

        var tokens = new FakeTokenService();
        var issuer = new AuthTokenIssuer(db, tokens, new FakeAppSettings());
        var handler = new RefreshTokenCommandHandler(db, tokens, issuer);

        // Issue the first session, then rotate it.
        var first = await issuer.IssueAsync(owner, tenant, ["Customers.View"], CancellationToken.None);
        var rotated = await handler.Handle(new RefreshTokenCommand(first.RefreshToken), CancellationToken.None);

        Assert.NotEqual(first.RefreshToken, rotated.RefreshToken);

        // Replaying the original (now-rotated) token must fail...
        await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            handler.Handle(new RefreshTokenCommand(first.RefreshToken), CancellationToken.None));

        // ...and revoke ALL sessions (the rotated token no longer works either).
        var dbOwner = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == owner.Id);
        Assert.Null(dbOwner.RefreshToken);

        await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            handler.Handle(new RefreshTokenCommand(rotated.RefreshToken), CancellationToken.None));
    }
}
