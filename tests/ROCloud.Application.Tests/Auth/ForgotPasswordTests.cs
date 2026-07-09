using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Auth.Commands.ForgotPassword;

namespace ROCloud.Application.Tests.Auth;

/// <summary>#5: a deactivated user must not be able to reset (reset-then-fail-login was a dead-end).</summary>
public class ForgotPasswordTests
{
    private sealed class CapturingEmail : IEmailService
    {
        public string? To { get; private set; }
        public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
        {
            To = to;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ActiveUser_ReceivesResetEmail()
    {
        var db = AuthTestHelpers.NewDb();
        var (_, owner) = await AuthTestHelpers.SeedAsync(db);
        var email = new CapturingEmail();

        await new ForgotPasswordCommandHandler(db, AuthTestHelpers.NewCache(), email, new FakeAppSettings())
            .Handle(new ForgotPasswordCommand(owner.Email, AuthTestHelpers.Subdomain), CancellationToken.None);

        Assert.Equal(owner.Email, email.To);
    }

    [Fact]
    public async Task DeactivatedUser_ReceivesNothing()
    {
        var db = AuthTestHelpers.NewDb();
        var (_, owner) = await AuthTestHelpers.SeedAsync(db);
        owner.IsActive = false;
        await db.SaveChangesAsync();
        var email = new CapturingEmail();

        await new ForgotPasswordCommandHandler(db, AuthTestHelpers.NewCache(), email, new FakeAppSettings())
            .Handle(new ForgotPasswordCommand(owner.Email, AuthTestHelpers.Subdomain), CancellationToken.None);

        Assert.Null(email.To);   // no reset email → no reset-then-fail-login trap
    }
}
