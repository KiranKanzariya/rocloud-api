using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Auth.Commands.FindWorkspace;
using ROCloud.Domain.Entities.Platform;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;

namespace ROCloud.Application.Tests.Auth;

public class FindWorkspaceTests
{
    private sealed class CapturingEmail : IEmailService
    {
        public List<(string To, string Subject, string Body)> Sent { get; } = [];
        public Task<bool> SendAsync(string to, string subject, string body, CancellationToken ct = default)
        {
            Sent.Add((to, subject, body));
            return Task.FromResult(true);
        }
    }

    private static AppDbContext NewDb()
        => new(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"findws-{Guid.NewGuid()}").Options,
            new TenantContext { TenantId = Guid.NewGuid() });

    private static async Task SeedTenantWithUserAsync(AppDbContext db, string sub, string email, bool active = true)
    {
        var plan = new Plan { Id = Guid.NewGuid(), Name = "Pro", PlanType = PlanType.Pro, MonthlyPrice = 999 };
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(), PlanId = plan.Id, Name = sub, Subdomain = sub,
            OwnerName = "Owner", OwnerEmail = email, OwnerMobile = "9", Status = TenantStatus.Active,
        };
        db.Plans.Add(plan);
        db.Tenants.Add(tenant);
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, Name = "Owner", Email = email,
            PasswordHash = "x", IsActive = active,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task EmailsAllMatchingWorkspaces_WithPortalUrls()
    {
        var db = NewDb();
        var email = "rajesh@example.com";
        await SeedTenantWithUserAsync(db, "sharma-ro", email);
        await SeedTenantWithUserAsync(db, "krishna", email);      // same email, different tenant
        await SeedTenantWithUserAsync(db, "other", "someone@else.com");

        var mailer = new CapturingEmail();
        await new FindWorkspaceCommandHandler(db, mailer, new FakeAppSettings())
            .Handle(new FindWorkspaceCommand(email), CancellationToken.None);

        var sent = Assert.Single(mailer.Sent);
        Assert.Equal(email, sent.To);
        Assert.Contains("https://sharma-ro.app.test", sent.Body);
        Assert.Contains("https://krishna.app.test", sent.Body);
        Assert.DoesNotContain("other.app.test", sent.Body);     // not this user's workspace
    }

    [Fact]
    public async Task UnknownEmail_SendsNothing_AndSucceeds()
    {
        var db = NewDb();
        await SeedTenantWithUserAsync(db, "sharma-ro", "rajesh@example.com");

        var mailer = new CapturingEmail();
        await new FindWorkspaceCommandHandler(db, mailer, new FakeAppSettings())
            .Handle(new FindWorkspaceCommand("nobody@nowhere.com"), CancellationToken.None);

        Assert.Empty(mailer.Sent);   // no enumeration signal, no mail
    }
}
