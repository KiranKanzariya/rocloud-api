using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Auth.Commands.ResetPassword;
using ROCloud.Application.Features.Users.Commands.InviteUser;
using ROCloud.Application.Tests.Auth;
using ROCloud.Domain.Entities.Platform;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;

namespace ROCloud.Application.Tests.Users;

/// <summary>The invite email now carries a working password-reset token (previously it had none).</summary>
public class InviteUserTests
{
    private sealed class CapturingEmail : IEmailService
    {
        public string? Body { get; private set; }
        public Task<bool> SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
        {
            Body = htmlBody;
            return Task.FromResult(true);
        }
    }

    private sealed class FakePasswordService : IPasswordService
    {
        public string Hash(string password) => "hash:" + password;
        public bool Verify(string password, string hash) => hash == "hash:" + password;
    }

    [Fact]
    public async Task Invite_EmailsWorkingResetLink_ThatSetsPassword()
    {
        var tenantId = Guid.NewGuid();
        var ctx = new TenantContext { TenantId = tenantId };
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"invite-{Guid.NewGuid()}").Options, ctx);
        var cache = AuthTestHelpers.NewCache();
        var email = new CapturingEmail();
        var passwords = new FakePasswordService();

        var planId = Guid.NewGuid();
        db.Plans.Add(new Plan { Id = planId, Name = "Pro", PlanType = PlanType.Pro, MaxUsers = 10 });
        db.Tenants.Add(new Tenant
        {
            Id = tenantId, PlanId = planId, Name = "Co", Subdomain = "acme",
            OwnerName = "O", OwnerEmail = "o@x.com", OwnerMobile = "9"
        });
        var roleId = Guid.NewGuid();
        db.Roles.Add(new Role { Id = roleId, TenantId = tenantId, Name = "Manager" });
        await db.SaveChangesAsync();

        var invitedId = await new InviteUserCommandHandler(
                db, ctx, passwords, email, cache, NullLogger<InviteUserCommandHandler>.Instance, new FakeAppSettings())
            .Handle(new InviteUserCommand("New Staff", "staff@x.com", null, roleId, null), CancellationToken.None);

        // Email links to the tenant's own portal reset page, with a token.
        Assert.NotNull(email.Body);
        Assert.Contains("acme", email.Body!);
        Assert.Contains("/reset-password?token=", email.Body);
        var token = email.Body.Split("token=")[1].Split('"')[0];

        // That token drives the existing reset flow and sets the invited user's password.
        await new ResetPasswordCommandHandler(db, cache, passwords)
            .Handle(new ResetPasswordCommand(token, "ChosenPass123"), CancellationToken.None);

        var user = await db.Users.FirstAsync(u => u.Id == invitedId);
        Assert.True(passwords.Verify("ChosenPass123", user.PasswordHash!));
    }
}
