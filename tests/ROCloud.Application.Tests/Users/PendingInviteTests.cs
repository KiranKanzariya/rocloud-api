using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Auth.Commands.ForgotPassword;
using ROCloud.Application.Features.Auth.Commands.Login;
using ROCloud.Application.Features.Auth.Commands.ResetPassword;
using ROCloud.Application.Features.Users.Commands.DeactivateUser;
using ROCloud.Application.Features.Users.Commands.InviteUser;
using ROCloud.Domain.Entities.Platform;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.Caching;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;

namespace ROCloud.Application.Tests.Users;

/// <summary>
/// A team member is PENDING until they accept their invitation.
///
/// <para>The scenario this exists for is a typo, not an attacker. An owner adds "Rajesh Sharma" and
/// mistypes the address as someone else's. Before this, that account was created live — the recipient
/// could set a password from the invitation, or simply use "forgot password", and walk into a business
/// they had nothing to do with as a Manager.</para>
///
/// <para>Now nothing works until the emailed link is opened, which is the only evidence we ever get
/// that the address belongs to the person who was added. A mistyped invitation stays inert and stays
/// visible to the owner as "Invited" — the part that makes the mistake noticeable rather than
/// silent.</para>
/// </summary>
public class PendingInviteTests
{
    private static readonly Guid TenantA = Guid.NewGuid();

    private sealed class FakePasswordService : IPasswordService
    {
        public string Hash(string password) => "hash:" + password;
        public bool Verify(string password, string hash) => hash == "hash:" + password;
    }

    /// <summary>Captures the invitation so a test can follow the link the way a recipient would.</summary>
    private sealed class CapturingEmailService : IEmailService
    {
        public string? LastBody { get; private set; }
        public Task<bool> SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
        {
            LastBody = htmlBody;
            return Task.FromResult(true);
        }
    }

    private static (AppDbContext Db, TenantContext Ctx) NewDb()
    {
        var ctx = new TenantContext { TenantId = TenantA };
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"invite-{Guid.NewGuid()}").Options, ctx);
        return (db, ctx);
    }

    private static async Task<Guid> SeedAsync(AppDbContext db)
    {
        var plan = new Plan { Id = Guid.NewGuid(), Name = "Pro", PlanType = PlanType.Pro, MaxUsers = 0, MaxDeliveryBoys = 0, IsActive = true };
        db.Plans.Add(plan);
        db.Tenants.Add(new Tenant
        {
            Id = TenantA, PlanId = plan.Id, Name = "Aqua RO", Subdomain = "aqua",
            OwnerName = "Ramesh Patel", OwnerEmail = "ramesh@gmail.com", OwnerMobile = "9",
            Status = TenantStatus.Active, DefaultLanguage = "en",
        });
        var roleId = Guid.NewGuid();
        db.Roles.Add(new Role { Id = roleId, TenantId = TenantA, Name = "Manager" });
        await db.SaveChangesAsync();
        return roleId;
    }

    /// <summary>Pulls the token out of the invitation email, as clicking the link would.</summary>
    private static string TokenFrom(string body)
    {
        var at = body.IndexOf("token=", StringComparison.Ordinal);
        Assert.True(at >= 0, "the invitation carried no token");
        var rest = body[(at + 6)..];
        var end = rest.IndexOfAny(['"', '&', ' ', '<']);
        return end < 0 ? rest : rest[..end];
    }

    private static async Task<(Guid UserId, string Token, InMemoryCacheService Cache, AppDbContext Db)> InviteAsync()
    {
        var (db, ctx) = NewDb();
        var roleId = await SeedAsync(db);
        var cache = Auth.AuthTestHelpers.NewCache();
        var email = new CapturingEmailService();

        var userId = await new InviteUserCommandHandler(
                db, ctx, new FakePasswordService(), email, cache,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<InviteUserCommandHandler>.Instance,
                new Auth.FakeAppSettings())
            .Handle(new InviteUserCommand("Rajesh Sharma", "akash@gmail.com", null, roleId, null), CancellationToken.None);

        return (userId, TokenFrom(email.LastBody!), cache, db);
    }

    [Fact]
    public async Task AnInvitedMemberIsNotActiveYet()
    {
        var (userId, _, _, db) = await InviteAsync();

        var user = await db.Users.FirstAsync(u => u.Id == userId);
        Assert.False(user.IsActive);
        Assert.Null(user.InviteAcceptedAt);
    }

    [Fact]
    public async Task AnInvitedMemberCannotSignIn_EvenWithTheRightEmail()
    {
        // The whole point: until the address is proved, the account is not a way in.
        var (_, _, _, db) = await InviteAsync();

        var handler = new LoginCommandHandler(
            db,
            new ROCloud.Infrastructure.Identity.PasswordService(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build()),
            new ROCloud.Application.Common.Security.LoginAttemptService(new Auth.FakeAppSettings()),
            new ROCloud.Application.Features.Auth.Services.AuthTokenIssuer(db, new Auth.FakeTokenService(), new Auth.FakeAppSettings(), new Auth.FakeDeviceContext()));

        await Assert.ThrowsAnyAsync<Exception>(() => handler.Handle(
            new LoginCommand("akash@gmail.com", "anything", "aqua"), CancellationToken.None));
    }

    [Fact]
    public async Task ForgotPasswordSendsNothingForAnInvitedMember()
    {
        // The second route in, and the one the owner would never see. ForgotPassword already refuses
        // inactive users, so a pending invite cannot be turned into a reset link.
        var (_, _, _, db) = await InviteAsync();
        var email = new CapturingEmailService();

        await new ForgotPasswordCommandHandler(
                db, Auth.AuthTestHelpers.NewCache(), email, new Auth.FakeAppSettings(),
                new ROCloud.Application.Common.Services.NotificationTemplateRenderer(db))
            .Handle(new ForgotPasswordCommand("akash@gmail.com", "aqua"), CancellationToken.None);

        Assert.Null(email.LastBody);
    }

    [Fact]
    public async Task AcceptingTheInvitationActivatesTheAccount()
    {
        var (userId, token, cache, db) = await InviteAsync();

        await new ResetPasswordCommandHandler(db, cache, new FakePasswordService())
            .Handle(new ResetPasswordCommand(token, "Str0ng!Pass1"), CancellationToken.None);

        var user = await db.Users.FirstAsync(u => u.Id == userId);
        Assert.True(user.IsActive);
        Assert.NotNull(user.InviteAcceptedAt);
        Assert.Equal("hash:Str0ng!Pass1", user.PasswordHash);
    }

    [Fact]
    public async Task DeactivatingAPendingMemberKillsTheirInvitation()
    {
        // A mistyped invitation that has been noticed must be retractable. Without this the link is
        // still live in a stranger's inbox and would switch the account on days later.
        var (userId, token, cache, db) = await InviteAsync();

        await new DeactivateUserCommandHandler(db, cache)
            .Handle(new DeactivateUserCommand(userId), CancellationToken.None);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            new ResetPasswordCommandHandler(db, cache, new FakePasswordService())
                .Handle(new ResetPasswordCommand(token, "Str0ng!Pass1"), CancellationToken.None));

        var user = await db.Users.FirstAsync(u => u.Id == userId);
        Assert.False(user.IsActive);
        Assert.Null(user.InviteAcceptedAt);
    }

    [Fact]
    public async Task AResetNeverReactivatesAMemberWhoWasSwitchedOff()
    {
        // Acceptance is guarded on InviteAcceptedAt, not IsActive. Someone the owner deliberately
        // deactivated must not be able to let themselves back in through the reset form.
        var (userId, token, cache, db) = await InviteAsync();

        // Accept first, so this is a real member...
        await new ResetPasswordCommandHandler(db, cache, new FakePasswordService())
            .Handle(new ResetPasswordCommand(token, "Str0ng!Pass1"), CancellationToken.None);
        // ...then the owner switches them off.
        await new DeactivateUserCommandHandler(db, cache)
            .Handle(new DeactivateUserCommand(userId), CancellationToken.None);

        // A fresh reset token (as if one were still outstanding) must not revive the account.
        var second = "AABBCC";
        await cache.SetAsync($"pwreset:{second}",
            new ROCloud.Application.Common.Security.PasswordResetToken(userId), TimeSpan.FromMinutes(10));

        await new ResetPasswordCommandHandler(db, cache, new FakePasswordService())
            .Handle(new ResetPasswordCommand(second, "An0ther!Pass"), CancellationToken.None);

        Assert.False((await db.Users.FirstAsync(u => u.Id == userId)).IsActive);
    }

    [Fact]
    public async Task TheInvitationTellsTheWrongRecipientToIgnoreIt()
    {
        // The one mitigation available for a typo already delivered: whoever receives it must know
        // that doing nothing is enough, and that no account is live yet.
        var (db, ctx) = NewDb();
        var roleId = await SeedAsync(db);
        var email = new CapturingEmailService();

        await new InviteUserCommandHandler(
                db, ctx, new FakePasswordService(), email, Auth.AuthTestHelpers.NewCache(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<InviteUserCommandHandler>.Instance,
                new Auth.FakeAppSettings())
            .Handle(new InviteUserCommand("Rajesh Sharma", "akash@gmail.com", null, roleId, null), CancellationToken.None);

        Assert.Contains("ignore this email", email.LastBody, StringComparison.OrdinalIgnoreCase);
    }
}
