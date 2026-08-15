using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Security;
using ROCloud.Application.Features.Auth.Commands.Logout;
using ROCloud.Application.Features.Auth.Commands.RevokeSession;
using ROCloud.Application.Features.Auth.Queries.GetSessions;
using ROCloud.Application.Features.Auth.Services;
using ROCloud.Infrastructure.Persistence;

namespace ROCloud.Application.Tests.Auth;

/// <summary>
/// Seeing your signed-in devices, and ending one you are not holding.
///
/// The rows have existed for a while — they are what lets a phone and a portal stay signed in
/// independently — but nothing ever showed them and nothing could end one remotely. That is the whole
/// question raised by app cloning: an OEM dual-app copy, a work profile, or a handset someone else had
/// for ten minutes each produce a perfectly ordinary extra session, and the only defence is being able
/// to see it and press a button.
/// </summary>
public class SessionManagementTests
{
    /// <summary>Stands in for the signed-in caller. Only the three claims these paths read.</summary>
    private sealed class FakeCurrentUser : ICurrentUserService
    {
        public bool IsAuthenticated => UserId is not null;
        public Guid? UserId { get; init; }
        public Guid? TenantId { get; init; }
        public Guid? SessionId { get; init; }
        public string? Jti { get; init; }
        public DateTime? AccessTokenExpiresAt { get; init; }
        public IReadOnlyCollection<string> Permissions => [];
    }

    private static AuthTokenIssuer Issuer(AppDbContext db, string? label = null) =>
        new(db, new FakeTokenService(), new FakeAppSettings(), new FakeDeviceContext { Label = label });

    // ─── listing ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Sessions_AreListedPerDevice_NotPerRotation()
    {
        // Rotation writes a new row every refresh under the same SessionId, so a phone signed in for a
        // week would otherwise appear ~168 times in its owner's device list.
        await using var db = AuthTestHelpers.NewDb();
        var (tenant, owner) = await AuthTestHelpers.SeedAsync(db);
        var tokens = new FakeTokenService();
        var issuer = Issuer(db, "Pixel 7");
        var refresher = new RefreshTokenCommandHandlerFactory(db, tokens, issuer);

        // Three rotations of ONE device. Each must use the token the previous one handed back:
        // presenting a spent token is a replay, and the point of this table is that it revokes the
        // device rather than being quietly accepted.
        var phone = await issuer.IssueAsync(owner, tenant, [], CancellationToken.None);
        var second = await refresher.RefreshAsync(phone.RefreshToken);
        var third = await refresher.RefreshAsync(second.RefreshToken);
        await refresher.RefreshAsync(third.RefreshToken);

        var sessions = await new GetSessionsQueryHandler(db, new FakeCurrentUser { UserId = owner.Id })
            .Handle(new GetSessionsQuery(), CancellationToken.None);

        Assert.Single(sessions);
        Assert.Equal("Pixel 7", sessions[0].Label);
    }

    [Fact]
    public async Task Sessions_MarkTheDeviceMakingTheRequest()
    {
        await using var db = AuthTestHelpers.NewDb();
        var (tenant, owner) = await AuthTestHelpers.SeedAsync(db);
        var issuer = Issuer(db);

        await issuer.IssueAsync(owner, tenant, [], CancellationToken.None);
        await issuer.IssueAsync(owner, tenant, [], CancellationToken.None);

        var rows = await db.UserSessions.Where(s => s.UserId == owner.Id).ToListAsync();
        var current = rows[1].SessionId;

        var sessions = await new GetSessionsQueryHandler(
                db, new FakeCurrentUser { UserId = owner.Id, SessionId = current })
            .Handle(new GetSessionsQuery(), CancellationToken.None);

        Assert.Equal(2, sessions.Count);
        Assert.Single(sessions, s => s.IsCurrent);
        // The current device sorts first, so it is never buried in a long list.
        Assert.True(sessions[0].IsCurrent);
        Assert.Equal(current, sessions[0].Id);
    }

    [Fact]
    public async Task Sessions_NeverIncludeAnotherUsers()
    {
        // user_sessions carries no tenant query filter by design — refresh runs where no tenant is
        // resolved — so the user scope in the handler IS the isolation. If it ever widens, this fails.
        await using var db = AuthTestHelpers.NewDb();
        var (tenant, owner) = await AuthTestHelpers.SeedAsync(db);
        var stranger = await AddUserAsync(db, tenant.Id, "stranger@acme.test");
        var issuer = Issuer(db);

        await issuer.IssueAsync(owner, tenant, [], CancellationToken.None);
        await issuer.IssueAsync(stranger, tenant, [], CancellationToken.None);

        var sessions = await new GetSessionsQueryHandler(db, new FakeCurrentUser { UserId = owner.Id })
            .Handle(new GetSessionsQuery(), CancellationToken.None);

        Assert.Single(sessions);
    }

    [Fact]
    public async Task Sessions_OmitOnesAlreadyRevoked()
    {
        await using var db = AuthTestHelpers.NewDb();
        var (tenant, owner) = await AuthTestHelpers.SeedAsync(db);
        var issuer = Issuer(db);

        await issuer.IssueAsync(owner, tenant, [], CancellationToken.None);
        var gone = await issuer.IssueAsync(owner, tenant, [], CancellationToken.None);
        var goneId = (await db.UserSessions.OrderBy(s => s.CreatedAt).LastAsync()).SessionId;

        await UserSessions.RevokeChainAsync(db, goneId, CancellationToken.None);
        await db.SaveChangesAsync();

        var sessions = await new GetSessionsQueryHandler(db, new FakeCurrentUser { UserId = owner.Id })
            .Handle(new GetSessionsQuery(), CancellationToken.None);

        Assert.Single(sessions);
        Assert.DoesNotContain(sessions, s => s.Id == goneId);
        Assert.NotNull(gone.RefreshToken);
    }

    // ─── remote revoke ────────────────────────────────────────────────────

    [Fact]
    public async Task RevokingADevice_EndsThatChain_AndLeavesTheOthers()
    {
        await using var db = AuthTestHelpers.NewDb();
        var (tenant, owner) = await AuthTestHelpers.SeedAsync(db);
        var issuer = Issuer(db);

        await issuer.IssueAsync(owner, tenant, [], CancellationToken.None);
        await issuer.IssueAsync(owner, tenant, [], CancellationToken.None);
        var target = (await db.UserSessions.OrderBy(s => s.CreatedAt).LastAsync()).SessionId;

        await new RevokeSessionCommandHandler(db, new FakeCurrentUser { UserId = owner.Id }, new SessionValidityService(db, AuthTestHelpers.NewCache()))
            .Handle(new RevokeSessionCommand(target), CancellationToken.None);

        var live = await db.UserSessions.Where(s => s.RevokedAt == null).ToListAsync();
        Assert.Single(live);
        Assert.NotEqual(target, live[0].SessionId);
    }

    [Fact]
    public async Task RevokingSomebodyElsesDevice_IsRefused()
    {
        // A session id is a plain GUID in a URL and RevokeChainAsync takes a bare one, so the
        // ownership check in the handler is the only thing between it and another account.
        await using var db = AuthTestHelpers.NewDb();
        var (tenant, owner) = await AuthTestHelpers.SeedAsync(db);
        var stranger = await AddUserAsync(db, tenant.Id, "stranger@acme.test");
        var issuer = Issuer(db);

        await issuer.IssueAsync(stranger, tenant, [], CancellationToken.None);
        var theirs = (await db.UserSessions.FirstAsync(s => s.UserId == stranger.Id)).SessionId;

        // NotFound, not Forbidden: a distinguishable answer would confirm that a guessed id belongs
        // to somebody.
        await Assert.ThrowsAsync<NotFoundException>(() =>
            new RevokeSessionCommandHandler(db, new FakeCurrentUser { UserId = owner.Id }, new SessionValidityService(db, AuthTestHelpers.NewCache()))
                .Handle(new RevokeSessionCommand(theirs), CancellationToken.None));

        Assert.All(await db.UserSessions.ToListAsync(), s => Assert.Null(s.RevokedAt));
    }

    [Fact]
    public async Task ARevokedDevice_StopsBeingHonouredImmediately_NotWhenItsTokenExpires()
    {
        // Revoking the chain stops that device REFRESHING, but its current access token would keep
        // working for the rest of its hour — the worst possible answer when the reason for revoking is
        // that somebody else has the phone.
        await using var db = AuthTestHelpers.NewDb();
        var (tenant, owner) = await AuthTestHelpers.SeedAsync(db);
        var issuer = Issuer(db);
        var validity = new SessionValidityService(db, AuthTestHelpers.NewCache());

        await issuer.IssueAsync(owner, tenant, [], CancellationToken.None);
        var session = (await db.UserSessions.FirstAsync()).SessionId;

        Assert.True(await validity.IsSessionLiveAsync(owner.Id, session));

        await new RevokeSessionCommandHandler(db, new FakeCurrentUser { UserId = owner.Id }, new SessionValidityService(db, AuthTestHelpers.NewCache()))
            .Handle(new RevokeSessionCommand(session), CancellationToken.None);

        // A fresh service, because the answer is cached for a minute — that TTL is the documented
        // bound on how long a revoke takes to bite, not a licence to never notice.
        Assert.False(await new SessionValidityService(db, AuthTestHelpers.NewCache())
            .IsSessionLiveAsync(owner.Id, session));
    }

    [Fact]
    public async Task RevokingADevice_TakesEffectAtOnce_EvenWithAWarmCache()
    {
        // The reported behaviour: signing a PHONE out from the portal took noticeably longer than
        // signing a browser out. The browser was never really faster — it holds its access token in
        // memory only, so any reload goes through refresh, which reads the row directly. The phone
        // persists its token, so it rides on the cached "still live" answer until the entry lapses.
        //
        // Priming the cache on revoke closes that gap. The TTL is still what guarantees correctness;
        // this only brings the effect forward.
        await using var db = AuthTestHelpers.NewDb();
        var (tenant, owner) = await AuthTestHelpers.SeedAsync(db);
        var cache = AuthTestHelpers.NewCache();
        var validity = new SessionValidityService(db, cache);

        await Issuer(db).IssueAsync(owner, tenant, [], CancellationToken.None);
        var session = (await db.UserSessions.FirstAsync()).SessionId;

        // Warm the cache, exactly as a device making requests would.
        Assert.True(await validity.IsSessionLiveAsync(owner.Id, session));

        await new RevokeSessionCommandHandler(db, new FakeCurrentUser { UserId = owner.Id }, validity)
            .Handle(new RevokeSessionCommand(session), CancellationToken.None);

        // Same service, same warm cache — no waiting for the TTL.
        Assert.False(await validity.IsSessionLiveAsync(owner.Id, session));
    }

    [Fact]
    public async Task Rotation_KeepsTheSessionLive_ThroughTheSwap()
    {
        // Guards the naive reading of IsSessionLiveAsync: rotation revokes the old row and writes a new
        // one under the same SessionId, and a check that only looked at the old row would sign every
        // device out an hour after it signed in.
        await using var db = AuthTestHelpers.NewDb();
        var (tenant, owner) = await AuthTestHelpers.SeedAsync(db);
        var tokens = new FakeTokenService();
        var issuer = Issuer(db);
        var refresher = new RefreshTokenCommandHandlerFactory(db, tokens, issuer);

        var device = await issuer.IssueAsync(owner, tenant, [], CancellationToken.None);
        var session = (await db.UserSessions.FirstAsync()).SessionId;

        await refresher.RefreshAsync(device.RefreshToken);

        Assert.True(await new SessionValidityService(db, AuthTestHelpers.NewCache())
            .IsSessionLiveAsync(owner.Id, session));
    }

    // ─── account-wide revocation ──────────────────────────────────────────

    [Fact]
    public async Task RevokingEveryDevice_AlsoInvalidatesAccessTokensAlreadyIssued()
    {
        // The half that used to be missing. Ending the refresh chains stops NEW access tokens being
        // minted but did nothing about the ones already out there, so a password reset, a deactivation
        // or a deletion took up to Jwt:AccessTokenExpiryMinutes to lock anybody out of anything.
        await using var db = AuthTestHelpers.NewDb();
        var (tenant, owner) = await AuthTestHelpers.SeedAsync(db);
        var issuer = Issuer(db);
        await issuer.IssueAsync(owner, tenant, [], CancellationToken.None);

        var issuedBefore = DateTime.UtcNow.AddMinutes(-30);
        Assert.True(await new SessionValidityService(db, AuthTestHelpers.NewCache())
            .IsAcceptableAsync(owner.Id, issuedBefore));

        await UserSessions.RevokeAllAsync(db, owner.Id, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.NotNull((await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == owner.Id)).SessionsValidFrom);
        Assert.False(await new SessionValidityService(db, AuthTestHelpers.NewCache())
            .IsAcceptableAsync(owner.Id, issuedBefore));
    }

    [Fact]
    public async Task ATokenIssuedRightAfterARevocation_IsStillAccepted()
    {
        // Resetting your own password signs you straight back in; the new session must not be caught
        // by the stamp that ended the old ones.
        await using var db = AuthTestHelpers.NewDb();
        var (_, owner) = await AuthTestHelpers.SeedAsync(db);

        await UserSessions.RevokeAllAsync(db, owner.Id, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.True(await new SessionValidityService(db, AuthTestHelpers.NewCache())
            .IsAcceptableAsync(owner.Id, DateTime.UtcNow));
    }

    // ─── sign-out ─────────────────────────────────────────────────────────

    [Fact]
    public async Task SigningOut_RevokesTheSession_EvenWithNoAuthenticatedUser()
    {
        // The endpoint is anonymous so it still works once the access token has expired — which is
        // exactly when people reach for it. The refresh token IS the credential: token_hash is UNIQUE
        // and only its holder can produce it. Scoping the lookup to a signed-in user, as it used to,
        // silently turned sign-out back into "clears the cookie, leaves the session live".
        await using var db = AuthTestHelpers.NewDb();
        var (tenant, owner) = await AuthTestHelpers.SeedAsync(db);
        var tokens = new FakeTokenService();
        var issued = await Issuer(db).IssueAsync(owner, tenant, [], CancellationToken.None);

        var handler = new LogoutCommandHandler(
            db,
            new FakeCurrentUser(),                       // nobody signed in — expired access token
            tokens,
            new TokenBlocklistService(AuthTestHelpers.NewCache()));

        await handler.Handle(new LogoutCommand(issued.RefreshToken), CancellationToken.None);

        Assert.All(await db.UserSessions.ToListAsync(), s => Assert.NotNull(s.RevokedAt));
    }

    [Fact]
    public async Task SigningOutOnOneDevice_LeavesTheOther()
    {
        await using var db = AuthTestHelpers.NewDb();
        var (tenant, owner) = await AuthTestHelpers.SeedAsync(db);
        var tokens = new FakeTokenService();
        var issuer = Issuer(db);

        var phone = await issuer.IssueAsync(owner, tenant, [], CancellationToken.None);
        await issuer.IssueAsync(owner, tenant, [], CancellationToken.None);

        await new LogoutCommandHandler(db, new FakeCurrentUser(), tokens,
                new TokenBlocklistService(AuthTestHelpers.NewCache()))
            .Handle(new LogoutCommand(phone.RefreshToken), CancellationToken.None);

        Assert.Single(await db.UserSessions.Where(s => s.RevokedAt == null).ToListAsync());
    }

    // ─── helpers ──────────────────────────────────────────────────────────

    private static async Task<Domain.Entities.Tenant.User> AddUserAsync(
        AppDbContext db, Guid tenantId, string email)
    {
        var user = new Domain.Entities.Tenant.User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Other",
            Email = email,
            IsActive = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    /// <summary>Thin wrapper so a test can rotate a token without restating the wiring each time.</summary>
    private sealed class RefreshTokenCommandHandlerFactory(
        AppDbContext db, FakeTokenService tokens, AuthTokenIssuer issuer)
    {
        public Task<Features.Auth.Common.AuthResult> RefreshAsync(string refreshToken) =>
            new Features.Auth.Commands.RefreshToken.RefreshTokenCommandHandler(db, tokens, issuer)
                .Handle(new Features.Auth.Commands.RefreshToken.RefreshTokenCommand(refreshToken),
                        CancellationToken.None);
    }
}
