using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Features.Customers.Dtos;
using ROCloud.Application.Features.Customers.Queries.GetCustomers;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;

namespace ROCloud.Application.Tests.Performance;

/// <summary>
/// Performance smoke for the customer list (guide §16). Uses the InMemory provider, so this
/// asserts the query shape stays efficient and paged rather than being a real-DB latency
/// benchmark — the &lt;200ms-on-1000-customers budget is validated against PostgreSQL separately.
/// </summary>
[Trait("Category", "Performance")]
public class CustomerQueryPerformanceTests
{
    private static readonly Guid TenantA = Guid.NewGuid();

    [Fact]
    public async Task GetCustomers_With1000Customers_PagesQuickly()
    {
        var ctx = new TenantContext { TenantId = TenantA };
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"perf-{Guid.NewGuid()}").Options, ctx);

        for (var i = 0; i < 1000; i++)
            db.Customers.Add(new Customer
            {
                Id = Guid.NewGuid(), TenantId = TenantA, Name = $"Customer {i:D4}", Mobile = $"9{i:D9}"
            });
        await db.SaveChangesAsync();

        var handler = new GetCustomersQueryHandler(db);

        var sw = Stopwatch.StartNew();
        var result = await handler.Handle(
            new GetCustomersQuery(new CustomerFilterDto { Page = 1, PageSize = 25 }), CancellationToken.None);
        sw.Stop();

        Assert.Equal(1000, result.TotalCount);
        Assert.Equal(25, result.Items.Count);          // only a page is materialised
        Assert.True(sw.ElapsedMilliseconds < 2000,
            $"Customer list took {sw.ElapsedMilliseconds}ms — expected well under 2s on InMemory.");
    }
}
