using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.ServiceRequests.Commands.AssignTechnician;
using ROCloud.Application.Features.ServiceRequests.Commands.CreateServiceRequest;
using ROCloud.Application.Features.ServiceRequests.Queries.GetMyServiceJobs;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Tests.ServiceRequests;

public class ServiceRequestTests
{
    private static readonly Guid TenantA = Guid.NewGuid();

    private static (AppDbContext Db, TenantContext Ctx) NewDb()
    {
        var ctx = new TenantContext { TenantId = TenantA };
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"service-requests-{Guid.NewGuid()}").Options, ctx);
        return (db, ctx);
    }

    private sealed class FakeCurrentUser : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public Guid? UserId { get; init; }
        public Guid? TenantId { get; init; }
        public string? Jti => null;
        public DateTime? AccessTokenExpiresAt => null;
        public IReadOnlyCollection<string> Permissions => Array.Empty<string>();
    }

    private static async Task<Guid> SeedCustomerAsync(AppDbContext db, string name = "Ravi", string mobile = "9")
    {
        var id = Guid.NewGuid();
        db.Customers.Add(new Customer { Id = id, TenantId = TenantA, Name = name, Mobile = mobile });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<Guid> SeedUserAsync(AppDbContext db, string roleName)
    {
        var roleId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        db.Roles.Add(new Role { Id = roleId, TenantId = TenantA, Name = roleName });
        db.Users.Add(new User { Id = userId, TenantId = TenantA, RoleId = roleId, Name = roleName + " User", IsActive = true });
        await db.SaveChangesAsync();
        return userId;
    }

    [Fact]
    public async Task CreateServiceRequest_GeneratesUniqueTicketNumber()
    {
        var (db, ctx) = NewDb();
        var customerId = await SeedCustomerAsync(db);
        var handler = new CreateServiceRequestCommandHandler(db, ctx);

        var id1 = await handler.Handle(new CreateServiceRequestCommand(
            customerId, "Filter leaking", null, nameof(ServiceType.Complaint), null, null), CancellationToken.None);
        var id2 = await handler.Handle(new CreateServiceRequestCommand(
            customerId, "Annual service", null, nameof(ServiceType.RoutineAMC), nameof(ServicePriority.Low),
            DateOnly.FromDateTime(DateTime.UtcNow)), CancellationToken.None);

        var sr1 = await db.ServiceRequests.FirstAsync(s => s.Id == id1);
        var sr2 = await db.ServiceRequests.FirstAsync(s => s.Id == id2);

        Assert.Equal("SR-0001", sr1.TicketNumber);
        Assert.Equal("SR-0002", sr2.TicketNumber);
        Assert.NotEqual(sr1.TicketNumber, sr2.TicketNumber);
        Assert.Equal(ServiceRequestStatus.Open, sr1.Status);
    }

    [Fact]
    public async Task AssignTechnician_OnlyToUserWithTechnicianRole_ValidatesRole()
    {
        var (db, ctx) = NewDb();
        var customerId = await SeedCustomerAsync(db);
        var technicianId = await SeedUserAsync(db, "Technician");
        var deliveryBoyId = await SeedUserAsync(db, "DeliveryBoy");

        var srId = await new CreateServiceRequestCommandHandler(db, ctx).Handle(
            new CreateServiceRequestCommand(customerId, "Job", null, nameof(ServiceType.FilterChange), null, null),
            CancellationToken.None);

        var handler = new AssignTechnicianCommandHandler(db);

        // A non-technician is rejected.
        await Assert.ThrowsAsync<ValidationException>(() =>
            handler.Handle(new AssignTechnicianCommand(srId, deliveryBoyId), CancellationToken.None));

        // A technician is accepted.
        await handler.Handle(new AssignTechnicianCommand(srId, technicianId), CancellationToken.None);
        var sr = await db.ServiceRequests.FirstAsync(s => s.Id == srId);
        Assert.Equal(technicianId, sr.AssignedTechId);
    }

    [Fact]
    public async Task GetMyJobs_AsTechnician_OnlyReturnsAssignedToMe()
    {
        var (db, _) = NewDb();
        var customerId = await SeedCustomerAsync(db);
        var me = Guid.NewGuid();
        var other = Guid.NewGuid();

        void AddSr(Guid? tech) => db.ServiceRequests.Add(new ServiceRequest
        {
            Id = Guid.NewGuid(), TenantId = TenantA, CustomerId = customerId,
            TicketNumber = $"SR-{Guid.NewGuid().ToString()[..4]}", Title = "Job",
            ServiceType = ServiceType.FilterChange, Status = ServiceRequestStatus.Open,
            AssignedTechId = tech
        });
        AddSr(me);
        AddSr(me);
        AddSr(other);
        AddSr(null);
        await db.SaveChangesAsync();

        var handler = new GetMyServiceJobsQueryHandler(
            db, new FakeCurrentUser { UserId = me, TenantId = TenantA });

        var jobs = await handler.Handle(new GetMyServiceJobsQuery(null), CancellationToken.None);

        Assert.Equal(2, jobs.Count);
        Assert.All(jobs, j => Assert.Equal(me, j.AssignedTechId));
    }
}
