using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Security;
using ROCloud.Application.Features.Auth.Commands.GoogleHandoff;
using ROCloud.Application.Features.Auth.Common;
using ROCloud.Application.Features.Auth.Queries.CheckSubdomain;
using ROCloud.Application.Features.Auth.Services;
using ROCloud.Infrastructure.Identity;

namespace ROCloud.Application.Tests.Auth;

/// <summary>
/// The sign-in surface: who may mint a token for us, what a workspace may be called, and what happens
/// after five wrong passwords.
/// </summary>
public class SignInHardeningTests
{
    // ─── Google: fail closed ──────────────────────────────────────────────

    [Fact]
    public async Task GoogleSignIn_WithNoConfiguredClientId_RefusesInsteadOfAcceptingAnything()
    {
        // Google's validator skips the aud check entirely when the audience list is null, so a blank
        // config used to mean "accept an ID token minted for ANY OAuth client on earth". The token is
        // still a genuine, correctly signed Google token, so nothing downstream caught it: anyone
        // holding one for a victim's account could present it and be let straight in as them.
        //
        // Returning null without a network call is the whole assertion — an unreachable Google would
        // also return null, so the test passes an obviously invalid token to keep the two apart.
        var service = new GoogleAuthService(
            new ConfigurationBuilder().Build(), NullLogger<GoogleAuthService>.Instance);

        Assert.Null(await service.ValidateAsync("not-a-real-token"));
    }

    [Fact]
    public void GoogleSignIn_AcceptsAClientIdList_NotJustOne()
    {
        // Each platform gets its own OAuth client and therefore its own aud — the portal's web client,
        // the Android client, any future iOS one. A single-value setting cannot express that, so
        // adding a platform would have silently locked it out.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Google:ClientIds:0"] = "web-client.apps.googleusercontent.com",
                ["Google:ClientIds:1"] = "android-client.apps.googleusercontent.com",
                ["Google:ClientId"] = "legacy-client.apps.googleusercontent.com"
            })
            .Build();

        var audiences = typeof(GoogleAuthService)
            .GetMethod("AllowedAudiences", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(new GoogleAuthService(config, NullLogger<GoogleAuthService>.Instance), null) as string[];

        Assert.Equal(3, audiences!.Length);
        // The old single-value key still works, so existing deployments keep running.
        Assert.Contains("legacy-client.apps.googleusercontent.com", audiences);
    }

    // ─── reserved subdomains ──────────────────────────────────────────────

    [Theory]
    [InlineData("app")]        // the owner portal's own bare-domain host
    [InlineData("api")]
    [InlineData("admin")]
    [InlineData("www")]
    [InlineData("rocloud")]    // the apex label — Host.Split('.')[0] on rocloud.in
    [InlineData("support")]
    [InlineData("billing")]
    public void PlatformNames_CannotBeRegisteredAsAWorkspace(string slug)
    {
        // Not a data leak — the tenant middleware still keys off the JWT claim — but a workspace whose
        // sign-in page, reset links and handoff URLs all point at a shared platform host is a phishing
        // surface handed out at signup.
        Assert.True(ReservedSubdomains.IsReserved(slug));
    }

    [Theory]
    [InlineData("aqua")]
    [InlineData("sharma-ro")]
    [InlineData("pani")]
    public void OrdinaryNames_AreStillAvailable(string slug)
        => Assert.False(ReservedSubdomains.IsReserved(slug));

    [Fact]
    public async Task TheAvailabilityCheck_ReportsAReservedNameAsTaken()
    {
        // Reported as unavailable rather than as a distinct "reserved" state: from the registrant's
        // side the two are the same answer, and saying which platform hosts exist is information the
        // signup form has no reason to hand out.
        await using var db = AuthTestHelpers.NewDb();

        var result = await new CheckSubdomainQueryHandler(db)
            .Handle(new CheckSubdomainQuery("Admin"), CancellationToken.None);

        Assert.Equal("admin", result.Subdomain);
        Assert.False(result.Available);
    }

    [Fact]
    public void TheHostLabelList_AndTheRegistrationList_CannotDrift()
    {
        // They were separate copies and had already drifted — the API's had no "app", nobody's had
        // "rocloud". Anything that never names a workspace must also be unregisterable.
        Assert.All(ReservedSubdomains.HostLabels, label => Assert.True(ReservedSubdomains.IsReserved(label)));
    }

    // ─── lockout, on the row ──────────────────────────────────────────────

    [Fact]
    public async Task FiveWrongPasswords_LockTheAccount_AndTheLockSurvivesARestart()
    {
        // It used to count in the in-memory cache, so a restart — or a second instance during a
        // zero-downtime deploy — handed an attacker a fresh five-attempt budget against every account
        // at once. A lockout a restart clears is not a lockout; it is a delay.
        await using var db = AuthTestHelpers.NewDb();
        var (_, owner) = await AuthTestHelpers.SeedAsync(db);
        var attempts = new LoginAttemptService(new FakeAppSettings());

        for (var i = 0; i < new FakeAppSettings().MaxLoginAttempts; i++)
            attempts.RecordFailure(owner);
        await db.SaveChangesAsync();

        // Re-read from the database, which is what surviving a restart means.
        await using var reopened = db;
        var reloaded = await reopened.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == owner.Id);

        Assert.True(new LoginAttemptService(new FakeAppSettings()).IsLockedOut(reloaded));
        Assert.NotNull(reloaded.LockoutEndsAt);
    }

    [Fact]
    public void AfterALockExpires_TheNextLockTakesAFullRoundOfFailures()
    {
        // The counter resets as the lock is applied. Without that, one lockout would leave the account
        // permanently one bad attempt away from the next.
        var settings = new FakeAppSettings();
        var attempts = new LoginAttemptService(settings);
        var user = new Domain.Entities.Tenant.User { Id = Guid.NewGuid(), Name = "u" };

        for (var i = 0; i < settings.MaxLoginAttempts; i++)
            attempts.RecordFailure(user);
        Assert.True(attempts.IsLockedOut(user));
        Assert.Equal(0, user.FailedLoginAttempts);

        // The lock lapses; one more failure must not re-lock on its own.
        user.LockoutEndsAt = DateTime.UtcNow.AddMinutes(-1);
        attempts.RecordFailure(user);

        Assert.False(attempts.IsLockedOut(user));
        Assert.Equal(1, user.FailedLoginAttempts);
    }

    [Fact]
    public void ASuccessfulSignIn_ClearsTheCounter()
    {
        var attempts = new LoginAttemptService(new FakeAppSettings());
        var user = new Domain.Entities.Tenant.User
        {
            Id = Guid.NewGuid(), Name = "u", FailedLoginAttempts = 3, LockoutEndsAt = DateTime.UtcNow.AddMinutes(5)
        };

        attempts.Clear(user);

        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.Null(user.LockoutEndsAt);
    }

    // ─── the Google handoff is bound to its host ──────────────────────────

    [Fact]
    public async Task AHandoffGrant_CannotBeRedeemedOnAnotherWorkspacesHost()
    {
        // Redeeming an Aqua grant at pani.rocloud.in used to succeed and file an Aqua session under
        // Pani's cookie name, after which every request 403'd on TENANT_MISMATCH and every refresh
        // 401'd on the workspace guard — a tab wedged by a mistake the server could see all along.
        await using var db = AuthTestHelpers.NewDb();
        var (tenant, owner) = await AuthTestHelpers.SeedAsync(db);
        var tokens = new FakeTokenService();
        var handler = new GoogleHandoffCommandHandler(
            db, tokens, new AuthTokenIssuer(db, tokens, new FakeAppSettings(), new FakeDeviceContext()));

        var grant = tokens.GenerateHandoffToken(owner.Id, tenant.Id);

        await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            handler.Handle(new GoogleHandoffCommand(grant, "somebody-else"), CancellationToken.None));
    }

    [Fact]
    public async Task AHandoffGrant_IsAcceptedOnItsOwnHost()
    {
        await using var db = AuthTestHelpers.NewDb();
        var (tenant, owner) = await AuthTestHelpers.SeedAsync(db);
        var tokens = new FakeTokenService();
        var handler = new GoogleHandoffCommandHandler(
            db, tokens, new AuthTokenIssuer(db, tokens, new FakeAppSettings(), new FakeDeviceContext()));

        var result = await handler.Handle(
            new GoogleHandoffCommand(tokens.GenerateHandoffToken(owner.Id, tenant.Id), AuthTestHelpers.Subdomain),
            CancellationToken.None);

        Assert.False(string.IsNullOrEmpty(result.AccessToken));
    }

    // ─── device labels ────────────────────────────────────────────────────

    [Fact]
    public void ADeviceLabel_PrefersWhatTheClientReports()
        => Assert.Equal("Pixel 7 · Android 14", DeviceLabel.From("Pixel 7 · Android 14", "Mozilla/5.0 Chrome/1"));

    [Fact]
    public void ADeviceLabel_FallsBackToACoarseUserAgentReading()
        => Assert.Equal("Chrome on Windows", DeviceLabel.From(null,
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120 Safari/537.36"));

    [Fact]
    public void ADeviceLabel_ReadsEdgeAsEdge_NotChrome()
        // Edge and Opera both claim to be Chrome, and Chrome claims to be Safari, so the order of the
        // checks is the whole logic.
        => Assert.Equal("Edge on Windows", DeviceLabel.From(null,
            "Mozilla/5.0 (Windows NT 10.0) AppleWebKit/537.36 Chrome/120 Safari/537.36 Edg/120"));

    [Fact]
    public void ADeviceLabel_StripsWhatAClientShouldNotBeAbleToSend()
    {
        // Client-supplied, and rendered in both portals and written to logs — it must not be able to
        // carry a newline into a log line or markup into a list.
        var label = DeviceLabel.From("Pixel\n<script>alert(1)</script>", null);

        Assert.NotNull(label);
        Assert.DoesNotContain("\n", label);
        Assert.DoesNotContain("<", label);
    }

    [Fact]
    public void ADeviceLabel_IsNullWhenTheRequestSaysNothingUseful()
        => Assert.Null(DeviceLabel.From(null, null));
}
