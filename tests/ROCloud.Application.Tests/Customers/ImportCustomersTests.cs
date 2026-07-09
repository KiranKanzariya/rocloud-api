using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Customers;
using ROCloud.Application.Features.Customers.Commands.CreateCustomer;
using ROCloud.Application.Features.Customers.Commands.ImportCustomers;
using ROCloud.Application.Features.Customers.Commands.SetCustomerOpeningBalance;
using ROCloud.Application.Features.Customers.Queries.GetCustomerJarBalance;
using ROCloud.Application.Features.CustomerSubscriptions.Commands.CreateCustomerSubscription;
using ROCloud.Domain.Entities.Platform;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;

namespace ROCloud.Application.Tests.Customers;

public class ImportCustomersTests
{
    private static readonly Guid TenantA = Guid.NewGuid();

    private static (AppDbContext Db, TenantContext Ctx) NewDb()
    {
        var ctx = new TenantContext { TenantId = TenantA };
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"import-{Guid.NewGuid()}").Options, ctx);
        return (db, ctx);
    }

    private sealed class FakeCurrentUser : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public Guid? UserId { get; init; } = Guid.NewGuid();
        public Guid? TenantId { get; init; } = TenantA;
        public string? Jti => null;
        public DateTime? AccessTokenExpiresAt => null;
        public IReadOnlyCollection<string> Permissions => Array.Empty<string>();
    }

    /// <summary>Routes the import's sub-commands to the real handlers against the shared in-memory db.</summary>
    private sealed class RoutingMediator(AppDbContext db, TenantContext ctx, ICurrentUserService user) : IMediator
    {
        // Commands that return a value (IRequest<T>).
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default) => request switch
        {
            CreateCustomerCommand c => (TResponse)(object)await new CreateCustomerCommandHandler(db, ctx).Handle(c, ct),
            CreateCustomerSubscriptionCommand c => (TResponse)(object)await new CreateCustomerSubscriptionCommandHandler(db, ctx).Handle(c, ct),
            _ => throw new NotSupportedException(request.GetType().Name),
        };

        // Void commands (IRequest) — MediatR resolves these to this overload, not the generic one above.
        public async Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest
        {
            if (request is SetCustomerOpeningBalanceCommand c)
                await new SetCustomerOpeningBalanceCommandHandler(db, ctx, user).Handle(c, ct);
            else throw new NotSupportedException(request.GetType().Name);
        }

        public Task<object?> Send(object request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task Publish(object notification, CancellationToken ct = default) => throw new NotSupportedException();
        public Task Publish<TNotification>(TNotification notification, CancellationToken ct = default) where TNotification : INotification => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static ImportCustomersCommandHandler NewHandler(AppDbContext db, TenantContext ctx) =>
        new(db, new RoutingMediator(db, ctx, new FakeCurrentUser()), ctx);

    private static async Task<Guid> SeedProductAsync(AppDbContext db, string name = "20L Jar", BottleSize size = BottleSize.TwentyL)
    {
        var id = Guid.NewGuid();
        db.Products.Add(new Product { Id = id, TenantId = TenantA, Name = name, BottleSize = size });
        await db.SaveChangesAsync();
        return id;
    }

    private const string Header =
        "name,mobile,delivery_mode,payment_preference,area,opening_jars_20l,opening_dues,sub_product_size,sub_qty,sub_frequency,sub_rate,sub_start_date";

    private static ImportCustomersCommand Cmd(string csv, bool dryRun) =>
        new(csv, dryRun, DateOnly.FromDateTime(DateTime.UtcNow));

    [Fact]
    public async Task DryRun_reports_outcomes_without_writing()
    {
        var (db, ctx) = NewDb();
        await SeedProductAsync(db);
        db.Customers.Add(new Customer { Id = Guid.NewGuid(), TenantId = TenantA, Name = "Existing", Mobile = "+919000000001" });
        await db.SaveChangesAsync();

        var csv = string.Join("\n",
            Header,
            "Ramesh,9876543210,HomeDelivery,Monthly,,3,450,,,,,",          // valid → Created
            "Bad Mode,9876543211,Flying,PerBottle,,,,,,,,",                // invalid enum → Failed
            "Dup,9000000001,,,,,,,,,,",                                    // existing mobile → Skipped
            "No Mobile Cust,,,,,,,,,,,",                                   // no mobile → Created (mobile optional)
            "First,9888888888,,,,,,,,,,",                                  // in-file dup base
            "Second,9888888888,,,,,,,,,,");                               // in-file dup → Skipped

        var result = await NewHandler(db, ctx).Handle(Cmd(csv, dryRun: true), CancellationToken.None);

        Assert.True(result.DryRun);
        Assert.Equal(6, result.Total);
        Assert.Equal(3, result.Created); // Ramesh + No Mobile Cust + First
        Assert.Equal(2, result.Skipped); // existing + in-file dup
        Assert.Equal(1, result.Failed);  // bad enum
        Assert.Equal("Failed", result.Rows.Single(r => r.Name == "Bad Mode").Status);
        Assert.Equal("Created", result.Rows.Single(r => r.Name == "No Mobile Cust").Status);

        // Nothing persisted beyond the pre-seeded customer.
        Assert.Equal(1, await db.Customers.CountAsync());
    }

    [Fact]
    public async Task Commit_creates_customer_opening_balance_and_subscription()
    {
        var (db, ctx) = NewDb();
        await SeedProductAsync(db);

        var csv = string.Join("\n",
            Header,
            "Ramesh Patel,98765 43210,HomeDelivery,Monthly,,3,450,20L,2,Daily,40,2026-07-01");

        var result = await NewHandler(db, ctx).Handle(Cmd(csv, dryRun: false), CancellationToken.None);

        Assert.Equal(1, result.Created);
        Assert.True(result.Rows.Single().Message is null, $"unexpected warning: {result.Rows.Single().Message}");
        var customer = Assert.Single(await db.Customers.ToListAsync());
        Assert.Equal("+919876543210", customer.Mobile); // canonical +91 form (space stripped)

        // Opening jars + dues applied.
        var jars = await new GetCustomerJarBalanceQueryHandler(db)
            .Handle(new GetCustomerJarBalanceQuery(customer.Id), CancellationToken.None);
        Assert.Equal(3, Assert.Single(jars).Outstanding);
        Assert.Equal(450m, await CustomerBalance.ComputeAsync(db, customer.Id, CancellationToken.None));

        // Subscription created.
        Assert.Single(await db.CustomerSubscriptions.ToListAsync());
    }

    [Fact]
    public async Task Skips_rows_with_duplicate_name()
    {
        var (db, ctx) = NewDb();
        db.Customers.Add(new Customer { Id = Guid.NewGuid(), TenantId = TenantA, Name = "Rajesh Kumar", Mobile = "+919000000001" });
        await db.SaveChangesAsync();

        var csv = string.Join("\n",
            Header,
            "Rajesh Kumar,9876543210,,,,,,,,,,",   // same name as existing (different mobile) → Skipped
            "rajesh kumar,9876543211,,,,,,,,,,",   // case-insensitive duplicate → Skipped
            "Suresh,9876543212,,,,,,,,,,");        // unique → Created

        var result = await NewHandler(db, ctx).Handle(Cmd(csv, dryRun: false), CancellationToken.None);

        Assert.Equal(1, result.Created);   // Suresh
        Assert.Equal(2, result.Skipped);   // existing-name match + case-insensitive duplicate
        Assert.Equal(0, result.Failed);
        Assert.Equal("A customer with this name already exists.",
            result.Rows.First(r => r.Name == "Rajesh Kumar").Message);
        Assert.Equal(2, await db.Customers.CountAsync()); // pre-seeded + Suresh
    }

    /// <summary>#5: import fills only up to the plan's remaining customer headroom.</summary>
    [Fact]
    public async Task Import_StopsAtPlanCustomerCap()
    {
        var (db, ctx) = NewDb();
        var plan = new Plan { Id = Guid.NewGuid(), Name = "Basic", PlanType = PlanType.Basic, MaxCustomers = 1 };
        db.Plans.Add(plan);
        db.Tenants.Add(new Tenant
        {
            Id = TenantA, PlanId = plan.Id, Name = "Co", Subdomain = "co",
            OwnerName = "O", OwnerEmail = "o@x.com", OwnerMobile = "9"
        });
        await db.SaveChangesAsync();

        var csv = string.Join("\n", Header,
            "Alpha,9876500001,,,,,,,,,,",
            "Beta,9876500002,,,,,,,,,,");

        var result = await NewHandler(db, ctx).Handle(Cmd(csv, dryRun: false), CancellationToken.None);

        Assert.Equal(1, result.Created);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(1, await db.Customers.CountAsync());
        Assert.Contains("customer limit", result.Rows.Single(r => r.Name == "Beta").Message!);
    }

    [Fact]
    public async Task Commit_creates_customer_without_mobile()
    {
        var (db, ctx) = NewDb();

        // Two mobile-less customers with different names — both created; mobile stored as NULL.
        var csv = string.Join("\n", Header,
            "Geeta Devi,,HomeDelivery,Monthly,,,,,,,,",
            "Shanti Bai,,,,,,,,,,,");

        var result = await NewHandler(db, ctx).Handle(Cmd(csv, dryRun: false), CancellationToken.None);

        Assert.Equal(2, result.Created);
        Assert.Equal(0, result.Failed);
        var customers = await db.Customers.OrderBy(c => c.Name).ToListAsync();
        Assert.Equal(2, customers.Count);
        Assert.All(customers, c => Assert.Null(c.Mobile));
        Assert.All(customers, c => Assert.NotNull(c.CustomerCode)); // still identified by CUST- code
    }

    [Fact]
    public async Task Invalid_mobile_is_rejected_not_treated_as_missing()
    {
        var (db, ctx) = NewDb();
        var csv = string.Join("\n", Header, "Bad Number,12345,,,,,,,,,,"); // 5 digits → unparseable

        var result = await NewHandler(db, ctx).Handle(Cmd(csv, dryRun: false), CancellationToken.None);

        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Failed);
        Assert.Equal("Invalid mobile number.", result.Rows.Single().Message);
    }

    [Fact]
    public async Task Skips_row_when_mobile_matches_legacy_non_plus91_format()
    {
        var (db, ctx) = NewDb();
        // Legacy customer stored with a bare 10-digit mobile (no +91 prefix).
        db.Customers.Add(new Customer { Id = Guid.NewGuid(), TenantId = TenantA, Name = "Ravi Kumar", Mobile = "9876543210" });
        await db.SaveChangesAsync();

        var csv = string.Join("\n", Header, "Ramesh Patel,9876543210,,,,,,,,,,");

        var result = await NewHandler(db, ctx).Handle(Cmd(csv, dryRun: false), CancellationToken.None);

        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Skipped);
        Assert.Equal("A customer with this mobile already exists.", result.Rows.Single().Message);
        Assert.Equal(1, await db.Customers.CountAsync()); // no duplicate created
    }

    [Fact]
    public async Task Commit_is_rerunnable_skips_existing()
    {
        var (db, ctx) = NewDb();
        await SeedProductAsync(db);
        var csv = string.Join("\n", Header, "Ramesh,9876543210,,,,,,,,,,");

        await NewHandler(db, ctx).Handle(Cmd(csv, dryRun: false), CancellationToken.None);
        var second = await NewHandler(db, ctx).Handle(Cmd(csv, dryRun: false), CancellationToken.None);

        Assert.Equal(0, second.Created);
        Assert.Equal(1, second.Skipped);
        Assert.Equal(1, await db.Customers.CountAsync()); // no duplicate
    }
}
