using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Security;
using ROCloud.Application.Features.Auth.Commands.Login;
using ROCloud.Application.Features.Auth.Services;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.Identity;
using ROCloud.Infrastructure.Persistence;

namespace ROCloud.Application.Tests.Auth;

/// <summary>
/// A blocked workspace (suspended, or cancelled past its paid period) can only be unblocked by the owner
/// paying. Staff used to be handed a session anyway and then hit a 401 on every request — an inescapable
/// dead-end. AuthTokenIssuer now refuses them at the door while still letting the OWNER in to pay.
/// </summary>
public class TenantBlockedSignInTests
{
    private const string StaffEmail = "driver@acme.test";

    private static AuthTokenIssuer NewIssuer(AppDbContext db)
        => new(db, new FakeTokenService(), new FakeAppSettings());

    /// <summary>Adds a non-owner (delivery boy) alongside the seeded Owner, with Role navigation loaded.</summary>
    private static async Task<User> AddStaffAsync(AppDbContext db, Guid tenantId)
    {
        var role = new Role { Id = Guid.NewGuid(), TenantId = tenantId, Name = "DeliveryBoy", IsSystem = true };
        db.Roles.Add(role);

        var staff = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RoleId = role.Id,
            Role = role,
            Name = "Driver",
            Email = StaffEmail,
            PasswordHash = new PasswordService(new ConfigurationBuilder().Build()).Hash(AuthTestHelpers.ValidPassword),
            AuthProvider = AuthProvider.Custom,
            IsActive = true
        };
        db.Users.Add(staff);
        await db.SaveChangesAsync();
        return staff;
    }

    [Fact]
    public async Task Suspended_RefusesANonOwner()
    {
        await using var db = AuthTestHelpers.NewDb();
        var (tenant, _) = await AuthTestHelpers.SeedAsync(db);
        var staff = await AddStaffAsync(db, tenant.Id);
        tenant.Status = TenantStatus.Suspended;

        var ex = await Assert.ThrowsAsync<TenantBlockedException>(
            () => NewIssuer(db).IssueAsync(staff, tenant, [], CancellationToken.None));

        Assert.Contains("account owner", ex.Message);   // tells them who can fix it
        Assert.Null(staff.RefreshToken);                // no session was established
    }

    [Fact]
    public async Task Suspended_StillLetsTheOwnerIn_SoTheyCanPay()
    {
        await using var db = AuthTestHelpers.NewDb();
        var (tenant, owner) = await AuthTestHelpers.SeedAsync(db);
        // Mirrors the handlers, which all load the user with IgnoreQueryFilters().Include(u => u.Role).
        owner.Role = await db.Roles.IgnoreQueryFilters().FirstAsync(r => r.Id == owner.RoleId);
        tenant.Status = TenantStatus.Suspended;

        var result = await NewIssuer(db).IssueAsync(owner, tenant, [], CancellationToken.None);

        Assert.False(string.IsNullOrEmpty(result.AccessToken));
    }

    [Fact]
    public async Task Overdue_DoesNotBlockStaff_TheyKeepWorkingThroughTheGraceWindow()
    {
        await using var db = AuthTestHelpers.NewDb();
        var (tenant, _) = await AuthTestHelpers.SeedAsync(db);
        var staff = await AddStaffAsync(db, tenant.Id);
        tenant.Status = TenantStatus.Overdue;
        tenant.SubscriptionEndsAt = DateTime.UtcNow.AddDays(-1);

        var result = await NewIssuer(db).IssueAsync(staff, tenant, [], CancellationToken.None);

        Assert.False(string.IsNullOrEmpty(result.AccessToken));
    }

    [Fact]
    public async Task Cancelled_LetsStaffInUntilThePaidPeriodEnds()
    {
        await using var db = AuthTestHelpers.NewDb();
        var (tenant, _) = await AuthTestHelpers.SeedAsync(db);
        var staff = await AddStaffAsync(db, tenant.Id);
        tenant.Status = TenantStatus.Cancelled;
        tenant.SubscriptionEndsAt = DateTime.UtcNow.AddDays(10);   // already paid for — keep working

        var result = await NewIssuer(db).IssueAsync(staff, tenant, [], CancellationToken.None);

        Assert.False(string.IsNullOrEmpty(result.AccessToken));
    }

    [Fact]
    public async Task Cancelled_RefusesStaffOnceThePaidPeriodHasEnded()
    {
        await using var db = AuthTestHelpers.NewDb();
        var (tenant, _) = await AuthTestHelpers.SeedAsync(db);
        var staff = await AddStaffAsync(db, tenant.Id);
        tenant.Status = TenantStatus.Cancelled;
        tenant.SubscriptionEndsAt = DateTime.UtcNow.AddDays(-1);

        await Assert.ThrowsAsync<TenantBlockedException>(
            () => NewIssuer(db).IssueAsync(staff, tenant, [], CancellationToken.None));
    }

    [Fact]
    public async Task LoginCommand_SurfacesTheBlock_RatherThanAFakeCredentialError()
    {
        await using var db = AuthTestHelpers.NewDb();
        var (tenant, _) = await AuthTestHelpers.SeedAsync(db);
        await AddStaffAsync(db, tenant.Id);
        tenant.Status = TenantStatus.Suspended;
        await db.SaveChangesAsync();

        var handler = new LoginCommandHandler(
            db,
            new PasswordService(new ConfigurationBuilder().Build()),
            new LoginAttemptService(AuthTestHelpers.NewCache(), new FakeAppSettings()),
            NewIssuer(db));

        // The password is correct — the refusal must not masquerade as bad credentials.
        await Assert.ThrowsAsync<TenantBlockedException>(() => handler.Handle(
            new LoginCommand(StaffEmail, AuthTestHelpers.ValidPassword, AuthTestHelpers.Subdomain),
            CancellationToken.None));
    }
}
