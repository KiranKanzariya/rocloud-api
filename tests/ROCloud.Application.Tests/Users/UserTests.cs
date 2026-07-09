using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Users.Commands.CreateUser;
using ROCloud.Application.Features.Users.Commands.DeactivateUser;
using ROCloud.Application.Features.Users.Commands.UpdateUser;
using ROCloud.Domain.Entities.Platform;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Tests.Users;

public class UserTests
{
    private static readonly Guid TenantA = Guid.NewGuid();

    private static (AppDbContext Db, TenantContext Ctx) NewDb()
    {
        var ctx = new TenantContext { TenantId = TenantA };
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"users-{Guid.NewGuid()}").Options, ctx);
        return (db, ctx);
    }

    private sealed class FakePasswordService : IPasswordService
    {
        public string Hash(string password) => "hash:" + password;
        public bool Verify(string password, string hash) => hash == "hash:" + password;
    }

    private sealed class NullEmailService : IEmailService
    {
        public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private static CreateUserCommandHandler NewCreateHandler(AppDbContext db, TenantContext ctx)
        => new(db, ctx, new FakePasswordService(), new NullEmailService());

    /// <summary>Seeds a plan (with the given user cap), the tenant, and a role; returns the role id.</summary>
    private static async Task<Guid> SeedTenantAsync(AppDbContext db, int maxUsers, string roleName = "DeliveryBoy")
    {
        var planId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        db.Plans.Add(new Plan { Id = planId, Name = "Basic", PlanType = PlanType.Basic, MaxUsers = maxUsers });
        db.Tenants.Add(new Tenant
        {
            Id = TenantA, PlanId = planId, Name = "Co", Subdomain = "co",
            OwnerName = "O", OwnerEmail = "o@x.com", OwnerMobile = "9"
        });
        db.Roles.Add(new Role { Id = roleId, TenantId = TenantA, Name = roleName });
        await db.SaveChangesAsync();
        return roleId;
    }

    [Fact]
    public async Task CreateUser_ExceedsPlanLimit_ThrowsPlanLimitException()
    {
        var (db, ctx) = NewDb();
        var roleId = await SeedTenantAsync(db, maxUsers: 1);
        db.Users.Add(new User { Id = Guid.NewGuid(), TenantId = TenantA, RoleId = roleId, Name = "Existing", Email = "e@x.com" });
        await db.SaveChangesAsync();

        var handler = NewCreateHandler(db, ctx);

        await Assert.ThrowsAsync<PlanLimitException>(() => handler.Handle(
            new CreateUserCommand("New Boy", "new@x.com", "9876543210", roleId, null, null), CancellationToken.None));
    }

    [Fact]
    public async Task CreateUser_DuplicateEmail_ReturnsValidationError()
    {
        var (db, ctx) = NewDb();
        var roleId = await SeedTenantAsync(db, maxUsers: 10);
        db.Users.Add(new User { Id = Guid.NewGuid(), TenantId = TenantA, RoleId = roleId, Name = "Existing", Email = "dup@x.com" });
        await db.SaveChangesAsync();

        var handler = NewCreateHandler(db, ctx);

        await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(
            new CreateUserCommand("Another", "DUP@x.com", null, roleId, null, null), CancellationToken.None));
    }

    [Fact]
    public async Task CreateUser_WithMultipleAreas_PersistsAll()
    {
        var (db, ctx) = NewDb();
        var roleId = await SeedTenantAsync(db, maxUsers: 10);
        var area1 = Guid.NewGuid();
        var area2 = Guid.NewGuid();
        db.Areas.Add(new Area { Id = area1, TenantId = TenantA, Name = "North" });
        db.Areas.Add(new Area { Id = area2, TenantId = TenantA, Name = "South" });
        await db.SaveChangesAsync();

        var id = await NewCreateHandler(db, ctx).Handle(
            new CreateUserCommand("Multi Boy", "multi@x.com", null, roleId, null, [area1, area2]),
            CancellationToken.None);

        var areas = await db.UserAreas.Where(ua => ua.UserId == id).Select(ua => ua.AreaId).ToListAsync();
        Assert.Equal(2, areas.Count);
        Assert.Contains(area1, areas);
        Assert.Contains(area2, areas);
    }

    [Fact]
    public async Task DeactivateUser_PreservesAuditHistory()
    {
        var (db, ctx) = NewDb();
        var roleId = await SeedTenantAsync(db, maxUsers: 10);
        var userId = Guid.NewGuid();
        db.Users.Add(new User { Id = userId, TenantId = TenantA, RoleId = roleId, Name = "Boy", Email = "b@x.com", IsActive = true, RefreshToken = "rt" });
        await db.SaveChangesAsync();

        await new DeactivateUserCommandHandler(db).Handle(new DeactivateUserCommand(userId), CancellationToken.None);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        Assert.NotNull(user);                 // row preserved (not deleted)
        Assert.False(user!.IsActive);
        Assert.False(user.IsDeleted);
        Assert.Null(user.RefreshToken);       // sessions revoked
    }

    /// <summary>Seeds the tenant + an "Owner" role and a "Manager" role; returns both role ids.</summary>
    private static async Task<(Guid OwnerRoleId, Guid ManagerRoleId)> SeedOwnerRolesAsync(AppDbContext db)
    {
        var planId = Guid.NewGuid();
        db.Plans.Add(new Plan { Id = planId, Name = "Basic", PlanType = PlanType.Basic, MaxUsers = 10 });
        db.Tenants.Add(new Tenant
        {
            Id = TenantA, PlanId = planId, Name = "Co", Subdomain = "co",
            OwnerName = "O", OwnerEmail = "o@x.com", OwnerMobile = "9"
        });
        var ownerRoleId = Guid.NewGuid();
        var managerRoleId = Guid.NewGuid();
        db.Roles.Add(new Role { Id = ownerRoleId, TenantId = TenantA, Name = "Owner" });
        db.Roles.Add(new Role { Id = managerRoleId, TenantId = TenantA, Name = "Manager" });
        await db.SaveChangesAsync();
        return (ownerRoleId, managerRoleId);
    }

    [Fact]
    public async Task UpdateUser_DemotingLastActiveOwner_Throws()
    {
        var (db, ctx) = NewDb();
        var (ownerRoleId, managerRoleId) = await SeedOwnerRolesAsync(db);
        var ownerId = Guid.NewGuid();
        db.Users.Add(new User { Id = ownerId, TenantId = TenantA, RoleId = ownerRoleId, Name = "Owner", Email = "owner@x.com", IsActive = true });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() => new UpdateUserCommandHandler(db, ctx).Handle(
            new UpdateUserCommand(ownerId, "Owner", null, managerRoleId, true, null), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateUser_DeactivatingLastActiveOwner_Throws()
    {
        var (db, ctx) = NewDb();
        var (ownerRoleId, _) = await SeedOwnerRolesAsync(db);
        var ownerId = Guid.NewGuid();
        db.Users.Add(new User { Id = ownerId, TenantId = TenantA, RoleId = ownerRoleId, Name = "Owner", Email = "owner@x.com", IsActive = true });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() => new UpdateUserCommandHandler(db, ctx).Handle(
            new UpdateUserCommand(ownerId, "Owner", null, ownerRoleId, false, null), CancellationToken.None));
    }

    /// <summary>#5: MaxDeliveryBoys is now enforced when creating a delivery-boy user.</summary>
    [Fact]
    public async Task CreateUser_ExceedsMaxDeliveryBoys_ThrowsPlanLimit()
    {
        var (db, ctx) = NewDb();
        var planId = Guid.NewGuid();
        db.Plans.Add(new Plan { Id = planId, Name = "Basic", PlanType = PlanType.Basic, MaxUsers = 10, MaxDeliveryBoys = 1 });
        db.Tenants.Add(new Tenant
        {
            Id = TenantA, PlanId = planId, Name = "Co", Subdomain = "co",
            OwnerName = "O", OwnerEmail = "o@x.com", OwnerMobile = "9"
        });
        var dbRoleId = Guid.NewGuid();
        db.Roles.Add(new Role { Id = dbRoleId, TenantId = TenantA, Name = "DeliveryBoy" });
        db.Users.Add(new User { Id = Guid.NewGuid(), TenantId = TenantA, RoleId = dbRoleId, Name = "DB1", Email = "db1@x.com", IsActive = true });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<PlanLimitException>(() => NewCreateHandler(db, ctx).Handle(
            new CreateUserCommand("DB2", "db2@x.com", "9876543210", dbRoleId, null, null), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateUser_DemotingOneOfTwoOwners_Succeeds()
    {
        var (db, ctx) = NewDb();
        var (ownerRoleId, managerRoleId) = await SeedOwnerRolesAsync(db);
        var owner1 = Guid.NewGuid();
        var owner2 = Guid.NewGuid();
        db.Users.Add(new User { Id = owner1, TenantId = TenantA, RoleId = ownerRoleId, Name = "O1", Email = "o1@x.com", IsActive = true });
        db.Users.Add(new User { Id = owner2, TenantId = TenantA, RoleId = ownerRoleId, Name = "O2", Email = "o2b@x.com", IsActive = true });
        await db.SaveChangesAsync();

        await new UpdateUserCommandHandler(db, ctx).Handle(
            new UpdateUserCommand(owner1, "O1", null, managerRoleId, true, null), CancellationToken.None);

        var u = await db.Users.FirstAsync(x => x.Id == owner1);
        Assert.Equal(managerRoleId, u.RoleId);   // demotion allowed — a second owner remains
    }
}
