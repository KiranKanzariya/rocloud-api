using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Features.Auth.Commands.RefreshToken;
using ROCloud.Application.Features.Auth.Services;

namespace ROCloud.Application.Tests.Auth;

/// <summary>
/// Sessions are per device.
///
/// They were not: a user had one refresh-token slot, so signing in on the phone overwrote the
/// portal's token, the portal's next refresh presented a token that no longer matched, the replay
/// check read that as theft and cleared the slot — and the phone was signed out too. One sign-in,
/// both products logged out, up to an access-token lifetime later.
/// </summary>
public class RefreshTokenCommandTests
{
    private static (RefreshTokenCommandHandler Handler, AuthTokenIssuer Issuer, FakeTokenService Tokens)
        Build(Infrastructure.Persistence.AppDbContext db)
    {
        var tokens = new FakeTokenService();
        var issuer = new AuthTokenIssuer(db, tokens, new FakeAppSettings());
        return (new RefreshTokenCommandHandler(db, tokens, issuer), issuer, tokens);
    }

    [Fact]
    public async Task TwoDevices_SignedInSeparately_BothKeepWorking()
    {
        // The reported bug, as a test: portal first, then the app on the same account.
        await using var db = AuthTestHelpers.NewDb();
        var (tenant, owner) = await AuthTestHelpers.SeedAsync(db);
        var (handler, issuer, _) = Build(db);

        var portal = await issuer.IssueAsync(owner, tenant, ["Customers.View"], CancellationToken.None);
        var app = await issuer.IssueAsync(owner, tenant, ["Customers.View"], CancellationToken.None);

        // Signing in on the app must not have disturbed the portal.
        var portalRefreshed = await handler.Handle(new RefreshTokenCommand(portal.RefreshToken), CancellationToken.None);
        var appRefreshed = await handler.Handle(new RefreshTokenCommand(app.RefreshToken), CancellationToken.None);

        Assert.NotEqual(portal.RefreshToken, portalRefreshed.RefreshToken);
        Assert.NotEqual(app.RefreshToken, appRefreshed.RefreshToken);

        // And they are genuinely two devices, not one row being handed back and forth.
        var sessions = await db.UserSessions.Where(s => s.UserId == owner.Id && s.RevokedAt == null).ToListAsync();
        Assert.Equal(2, sessions.Select(s => s.SessionId).Distinct().Count());
    }

    [Fact]
    public async Task RotatedToken_ReplayedOnOneDevice_LeavesTheOtherAlone()
    {
        // Replay still ends the device it belongs to — that part is the theft defence and stays.
        // What must NOT happen any more is it taking every other device with it.
        await using var db = AuthTestHelpers.NewDb();
        var (tenant, owner) = await AuthTestHelpers.SeedAsync(db);
        var (handler, issuer, _) = Build(db);

        var portal = await issuer.IssueAsync(owner, tenant, ["Customers.View"], CancellationToken.None);
        var app = await issuer.IssueAsync(owner, tenant, ["Customers.View"], CancellationToken.None);

        var portalRotated = await handler.Handle(new RefreshTokenCommand(portal.RefreshToken), CancellationToken.None);

        // The superseded portal token comes back.
        await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            handler.Handle(new RefreshTokenCommand(portal.RefreshToken), CancellationToken.None));

        // That device is finished, including the token it had rotated to.
        await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            handler.Handle(new RefreshTokenCommand(portalRotated.RefreshToken), CancellationToken.None));

        // The app never touched any of it.
        var stillWorks = await handler.Handle(new RefreshTokenCommand(app.RefreshToken), CancellationToken.None);
        Assert.NotEqual(app.RefreshToken, stillWorks.RefreshToken);
    }

    [Fact]
    public async Task Rotation_KeepsTheDeviceIdentity()
    {
        await using var db = AuthTestHelpers.NewDb();
        var (tenant, owner) = await AuthTestHelpers.SeedAsync(db);
        var (handler, issuer, _) = Build(db);

        var first = await issuer.IssueAsync(owner, tenant, ["Customers.View"], CancellationToken.None);
        await handler.Handle(new RefreshTokenCommand(first.RefreshToken), CancellationToken.None);

        // One device, two rows: the spent one and the current one, under the same session id.
        var rows = await db.UserSessions.Where(s => s.UserId == owner.Id).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Single(rows.Select(s => s.SessionId).Distinct());
        Assert.Single(rows.Where(s => s.RevokedAt == null));
    }

    [Fact]
    public async Task ExpiredSession_IsRefused()
    {
        await using var db = AuthTestHelpers.NewDb();
        var (tenant, owner) = await AuthTestHelpers.SeedAsync(db);
        var (handler, issuer, _) = Build(db);

        var session = await issuer.IssueAsync(owner, tenant, ["Customers.View"], CancellationToken.None);

        var row = await db.UserSessions.FirstAsync(s => s.UserId == owner.Id);
        row.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            handler.Handle(new RefreshTokenCommand(session.RefreshToken), CancellationToken.None));
    }

    [Fact]
    public async Task LegacySession_IsAdoptedOnce_ThenBehavesLikeAnyOther()
    {
        // TRANSITIONAL, and delete with the fallback it covers. Sessions that are live at the moment
        // this ships live in users.refresh_token; rejecting them would sign every user of both
        // products out on deploy day.
        await using var db = AuthTestHelpers.NewDb();
        var (_, owner) = await AuthTestHelpers.SeedAsync(db);
        var (handler, _, tokens) = Build(db);

        var legacy = $"{owner.Id}.legacy-token";
        owner.RefreshToken = tokens.HashRefreshToken(legacy);
        owner.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(30);
        await db.SaveChangesAsync();

        var adopted = await handler.Handle(new RefreshTokenCommand(legacy), CancellationToken.None);
        Assert.NotEqual(legacy, adopted.RefreshToken);

        // The column is cleared as it is adopted, so the same token cannot be presented twice.
        var dbOwner = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == owner.Id);
        Assert.Null(dbOwner.RefreshToken);
        await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            handler.Handle(new RefreshTokenCommand(legacy), CancellationToken.None));

        // And the device it became is an ordinary session.
        Assert.Single(await db.UserSessions.Where(s => s.UserId == owner.Id && s.RevokedAt == null).ToListAsync());
    }
}
