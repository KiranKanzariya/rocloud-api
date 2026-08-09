using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Deliveries.Commands.StartRoute;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Tests.Deliveries;

/// <summary>
/// "Start route" — one tap marks a delivery boy's whole day In Transit.
///
/// <para>In Transit is recorded for the OWNER's board, not for the delivery boy, so charging him a tap
/// per stop for it is work done on someone else's behalf. This command is what lets My Route drop the
/// per-stop button without emptying the owner's middle column.</para>
///
/// <para>Because it writes many rows from one tap, the guards matter more than usual: it must never
/// touch another boy's stops, never move a completed stop backwards, and never fail a whole route over
/// one order that cannot be in transit.</para>
/// </summary>
public class StartRouteTests
{
    private static readonly Guid TenantA = Guid.NewGuid();

    private static (AppDbContext Db, TenantContext Ctx) NewDb()
    {
        var ctx = new TenantContext { TenantId = TenantA };
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"startroute-{Guid.NewGuid()}").Options, ctx);
        return (db, ctx);
    }

    private sealed class FakeCurrentUser : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public Guid? UserId { get; init; }
        public Guid? TenantId { get; init; }
        public string? Jti => null;
        public DateTime? AccessTokenExpiresAt => null;
        public IReadOnlyCollection<string> Permissions { get; init; } = ["Deliveries.ViewOwn", "Deliveries.Update"];
    }

    private static readonly DateOnly Today = AppTimeZone.Today(DateTime.UtcNow);

    private static async Task<Delivery> SeedStopAsync(
        AppDbContext db, Guid boyId, DeliveryStatus status = DeliveryStatus.Pending,
        DeliveryMode mode = DeliveryMode.HomeDelivery, DateOnly? date = null)
    {
        var customerId = Guid.NewGuid();
        db.Customers.Add(new Customer { Id = customerId, TenantId = TenantA, Name = "Cust", Mobile = "9" });
        var order = new Order
        {
            Id = Guid.NewGuid(), TenantId = TenantA, CustomerId = customerId,
            OrderDate = date ?? Today, Status = OrderStatus.Pending,
            DeliveryMode = mode, DeliveryBoyId = boyId,
        };
        var delivery = new Delivery
        {
            Id = Guid.NewGuid(), TenantId = TenantA, OrderId = order.Id,
            DeliveryBoyId = boyId, ScheduledDate = date ?? Today, Status = status,
        };
        db.Orders.Add(order);
        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync();
        return delivery;
    }

    private static StartRouteCommandHandler Handler(AppDbContext db, Guid boyId) =>
        new(db, new FakeCurrentUser { UserId = boyId, TenantId = TenantA });

    [Fact]
    public async Task StartsEveryPendingStopOfTheDay_AndSyncsTheOrders()
    {
        var (db, _) = NewDb();
        var boy = Guid.NewGuid();
        await SeedStopAsync(db, boy);
        await SeedStopAsync(db, boy);
        await SeedStopAsync(db, boy);

        var started = await Handler(db, boy).Handle(new StartRouteCommand(Today), CancellationToken.None);

        Assert.Equal(3, started);
        Assert.All(await db.Deliveries.ToListAsync(), d => Assert.Equal(DeliveryStatus.InTransit, d.Status));
        // The order carries the status too — it is what the customer-facing screens read.
        Assert.All(await db.Orders.ToListAsync(), o => Assert.Equal(OrderStatus.InTransit, o.Status));
    }

    [Fact]
    public async Task LeavesAnotherDeliveryBoysStopsAlone()
    {
        // One tap writing many rows is exactly where a missing scope check does the most damage.
        var (db, _) = NewDb();
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        await SeedStopAsync(db, mine);
        var other = await SeedStopAsync(db, theirs);

        var started = await Handler(db, mine).Handle(new StartRouteCommand(Today), CancellationToken.None);

        Assert.Equal(1, started);
        Assert.Equal(DeliveryStatus.Pending, (await db.Deliveries.FirstAsync(d => d.Id == other.Id)).Status);
    }

    [Fact]
    public async Task NeverMovesACompletedStopBackwards()
    {
        var (db, _) = NewDb();
        var boy = Guid.NewGuid();
        var delivered = await SeedStopAsync(db, boy, DeliveryStatus.Delivered);
        var failed = await SeedStopAsync(db, boy, DeliveryStatus.Failed);
        await SeedStopAsync(db, boy);

        var started = await Handler(db, boy).Handle(new StartRouteCommand(Today), CancellationToken.None);

        Assert.Equal(1, started);
        Assert.Equal(DeliveryStatus.Delivered, (await db.Deliveries.FirstAsync(d => d.Id == delivered.Id)).Status);
        Assert.Equal(DeliveryStatus.Failed, (await db.Deliveries.FirstAsync(d => d.Id == failed.Id)).Status);
    }

    [Fact]
    public async Task SkipsPlantPickup_RatherThanFailingTheWholeRoute()
    {
        // The customer collects these — the van never carries them, and the per-stop command rejects
        // InTransit outright. A mixed day must still start.
        var (db, _) = NewDb();
        var boy = Guid.NewGuid();
        var pickup = await SeedStopAsync(db, boy, mode: DeliveryMode.PlantPickup);
        await SeedStopAsync(db, boy);

        var started = await Handler(db, boy).Handle(new StartRouteCommand(Today), CancellationToken.None);

        Assert.Equal(1, started);
        Assert.Equal(DeliveryStatus.Pending, (await db.Deliveries.FirstAsync(d => d.Id == pickup.Id)).Status);
    }

    [Fact]
    public async Task IgnoresOtherDays()
    {
        var (db, _) = NewDb();
        var boy = Guid.NewGuid();
        var yesterday = await SeedStopAsync(db, boy, date: Today.AddDays(-1));
        await SeedStopAsync(db, boy);

        var started = await Handler(db, boy).Handle(new StartRouteCommand(Today), CancellationToken.None);

        Assert.Equal(1, started);
        Assert.Equal(DeliveryStatus.Pending, (await db.Deliveries.FirstAsync(d => d.Id == yesterday.Id)).Status);
    }

    [Fact]
    public async Task ATomorrowRouteCannotBeStarted()
    {
        // The rollover job writes tomorrow's stops tonight. Starting them would put the board into a
        // state the per-stop updates then refuse to action.
        var (db, _) = NewDb();
        var boy = Guid.NewGuid();
        await SeedStopAsync(db, boy, date: Today.AddDays(1));

        await Assert.ThrowsAsync<ValidationException>(() =>
            Handler(db, boy).Handle(new StartRouteCommand(Today.AddDays(1)), CancellationToken.None));
    }

    [Fact]
    public async Task TappingItTwiceIsHarmless()
    {
        var (db, _) = NewDb();
        var boy = Guid.NewGuid();
        await SeedStopAsync(db, boy);

        var first = await Handler(db, boy).Handle(new StartRouteCommand(Today), CancellationToken.None);
        var second = await Handler(db, boy).Handle(new StartRouteCommand(Today), CancellationToken.None);

        Assert.Equal(1, first);
        Assert.Equal(0, second);   // nothing left in Pending — reported honestly, not as a fresh start
    }

    [Fact]
    public async Task DefaultsToToday_WhenNoDateIsGiven()
    {
        var (db, _) = NewDb();
        var boy = Guid.NewGuid();
        await SeedStopAsync(db, boy);

        var started = await Handler(db, boy).Handle(new StartRouteCommand(null), CancellationToken.None);

        Assert.Equal(1, started);
    }

    [Fact]
    public async Task AnUnauthenticatedCallerIsRefused()
    {
        var (db, _) = NewDb();
        var handler = new StartRouteCommandHandler(db, new FakeCurrentUser { UserId = null, TenantId = TenantA });

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            handler.Handle(new StartRouteCommand(Today), CancellationToken.None));
    }
}
