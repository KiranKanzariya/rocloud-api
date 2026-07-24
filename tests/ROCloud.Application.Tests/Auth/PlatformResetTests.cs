using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Platform.Auth.Commands.PlatformForgotPassword;
using ROCloud.Application.Features.Platform.Auth.Commands.PlatformResetPassword;
using ROCloud.Domain.Entities.Platform;
using ROCloud.Infrastructure.Identity;
using ROCloud.Infrastructure.Persistence;

namespace ROCloud.Application.Tests.Auth;

/// <summary>Platform-admin password recovery (fixes the "sole SuperAdmin locked out, DB-only" dead-end).</summary>
public class PlatformResetTests
{
    private sealed class CapturingEmail : IEmailService
    {
        public string? To { get; private set; }
        public string? Body { get; private set; }

        public Task<bool> SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
        {
            To = to;
            Body = htmlBody;
            return Task.FromResult(true);
        }
    }

    private static PasswordService Passwords() => new(new ConfigurationBuilder().Build());

    private static async Task<PlatformUser> SeedAdminAsync(AppDbContext db, bool active = true, string email = "admin@rocloud.test")
    {
        var u = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Name = "Admin",
            Email = email,
            PasswordHash = Passwords().Hash("OldPass12345"),
            PlatformRole = "SuperAdmin",
            IsActive = active,
        };
        db.PlatformUsers.Add(u);
        await db.SaveChangesAsync();
        return u;
    }

    // Email body ends "...: {token}" — the token is the trailing whitespace-free chunk.
    private static string ExtractToken(string body) => body[(body.LastIndexOf(' ') + 1)..];

    [Fact]
    public async Task ForgotThenReset_SetsNewPassword_AndTokenIsSingleUse()
    {
        var db = AuthTestHelpers.NewDb();
        var cache = AuthTestHelpers.NewCache();
        var email = new CapturingEmail();
        var admin = await SeedAdminAsync(db);

        await new PlatformForgotPasswordCommandHandler(db, cache, email, new FakeAppSettings())
            .Handle(new PlatformForgotPasswordCommand(admin.Email), CancellationToken.None);

        Assert.Equal(admin.Email, email.To);
        var token = ExtractToken(email.Body!);
        Assert.Equal(64, token.Length);

        await new PlatformResetPasswordCommandHandler(db, cache, Passwords())
            .Handle(new PlatformResetPasswordCommand(token, "BrandNewPass123"), CancellationToken.None);

        var updated = await db.PlatformUsers.FirstAsync(u => u.Id == admin.Id);
        Assert.True(Passwords().Verify("BrandNewPass123", updated.PasswordHash!));   // new password works
        Assert.Null(updated.RefreshToken);                                           // sessions revoked

        // Single-use: the same token can't be replayed.
        await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            new PlatformResetPasswordCommandHandler(db, cache, Passwords())
                .Handle(new PlatformResetPasswordCommand(token, "AnotherPass123"), CancellationToken.None));
    }

    [Fact]
    public async Task Reset_WithBadToken_Throws()
    {
        var db = AuthTestHelpers.NewDb();
        await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            new PlatformResetPasswordCommandHandler(db, AuthTestHelpers.NewCache(), Passwords())
                .Handle(new PlatformResetPasswordCommand("BOGUS-TOKEN", "SomePass12345"), CancellationToken.None));
    }

    [Fact]
    public async Task Forgot_InactiveAdmin_SendsNothing()
    {
        var db = AuthTestHelpers.NewDb();
        var email = new CapturingEmail();
        var admin = await SeedAdminAsync(db, active: false, email: "inactive@rocloud.test");

        await new PlatformForgotPasswordCommandHandler(db, AuthTestHelpers.NewCache(), email, new FakeAppSettings())
            .Handle(new PlatformForgotPasswordCommand(admin.Email), CancellationToken.None);

        Assert.Null(email.To);   // deactivated admin must not get a reset email
    }

    [Fact]
    public async Task Forgot_UnknownEmail_SendsNothing_NoLeak()
    {
        var db = AuthTestHelpers.NewDb();
        var email = new CapturingEmail();

        await new PlatformForgotPasswordCommandHandler(db, AuthTestHelpers.NewCache(), email, new FakeAppSettings())
            .Handle(new PlatformForgotPasswordCommand("nobody@nowhere.test"), CancellationToken.None);

        Assert.Null(email.To);
    }
}
