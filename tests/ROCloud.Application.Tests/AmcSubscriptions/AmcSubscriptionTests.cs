using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Features.AmcSubscriptions.Commands.CreateAmcSubscription;
using ROCloud.Application.Features.ServiceRequests.Commands.ScheduleAmcVisits;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;

namespace ROCloud.Application.Tests.AmcSubscriptions;

public class AmcSubscriptionTests
{
    private static readonly Guid TenantA = Guid.NewGuid();

    private static (AppDbContext Db, TenantContext Ctx) NewDb()
    {
        var ctx = new TenantContext { TenantId = TenantA };
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"amc-{Guid.NewGuid()}").Options, ctx);
        return (db, ctx);
    }

    private static async Task<Guid> SeedCustomerAsync(AppDbContext db, string mobile = "9")
    {
        var id = Guid.NewGuid();
        db.Customers.Add(new Customer { Id = id, TenantId = TenantA, Name = "Ravi", Mobile = mobile });
        await db.SaveChangesAsync();
        return id;
    }

    private static void SeedSubscription(AppDbContext db, Guid customerId, int interval, DateOnly nextDue, bool active = true)
        => db.AmcSubscriptions.Add(new AmcSubscription
        {
            Id = Guid.NewGuid(),
            TenantId = TenantA,
            CustomerId = customerId,
            IntervalMonths = interval,
            Amount = 1200m,
            StartDate = nextDue.AddMonths(-interval),
            NextDueDate = nextDue,
            IsActive = active
        });

    [Fact]
    public async Task CreateAmcSubscription_ComputesNextDueDate()
    {
        var (db, ctx) = NewDb();
        var customerId = await SeedCustomerAsync(db);
        var start = new DateOnly(2026, 1, 1);

        var handler = new CreateAmcSubscriptionCommandHandler(db, ctx);
        var id = await handler.Handle(new CreateAmcSubscriptionCommand(
            customerId, "Annual AMC", 3, 1200m, start, null, null), CancellationToken.None);

        var sub = await db.AmcSubscriptions.FirstAsync(s => s.Id == id);
        Assert.Equal(start.AddMonths(3), sub.NextDueDate);   // start + interval
        Assert.True(sub.IsActive);
    }

    [Fact]
    public async Task ScheduleAmcVisits_OnlySchedulesDueSubscriptions()
    {
        var (db, ctx) = NewDb();
        var dueCustomer = await SeedCustomerAsync(db, "1");
        var notDueCustomer = await SeedCustomerAsync(db, "2");
        var inactiveCustomer = await SeedCustomerAsync(db, "3");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        SeedSubscription(db, dueCustomer, 3, today.AddDays(3));        // within 7-day lead → due
        SeedSubscription(db, notDueCustomer, 6, today.AddMonths(2));   // far future → not due
        SeedSubscription(db, inactiveCustomer, 12, today, active: false); // due date but inactive
        await db.SaveChangesAsync();

        var result = await new ScheduleAmcVisitsCommandHandler(db, ctx)
            .Handle(new ScheduleAmcVisitsCommand(null, null), CancellationToken.None);

        Assert.Equal(1, result.VisitsCreated);
        var tickets = await db.ServiceRequests.Where(s => s.ServiceType == ServiceType.RoutineAMC).ToListAsync();
        Assert.Single(tickets);
        Assert.Equal(dueCustomer, tickets[0].CustomerId);
        Assert.Equal(today.AddDays(3), tickets[0].ScheduledDate);
    }

    [Fact]
    public async Task ScheduleAmcVisits_AdvancesNextDueDate()
    {
        var (db, ctx) = NewDb();
        var customerId = await SeedCustomerAsync(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var originalDue = today.AddDays(2);
        SeedSubscription(db, customerId, 6, originalDue);
        await db.SaveChangesAsync();

        await new ScheduleAmcVisitsCommandHandler(db, ctx)
            .Handle(new ScheduleAmcVisitsCommand(null, null), CancellationToken.None);

        var sub = await db.AmcSubscriptions.FirstAsync(s => s.CustomerId == customerId);
        Assert.Equal(originalDue.AddMonths(6), sub.NextDueDate);

        // A second run on the same day creates nothing more (now due far in the future).
        var second = await new ScheduleAmcVisitsCommandHandler(db, ctx)
            .Handle(new ScheduleAmcVisitsCommand(null, null), CancellationToken.None);
        Assert.Equal(0, second.VisitsCreated);
    }
}
