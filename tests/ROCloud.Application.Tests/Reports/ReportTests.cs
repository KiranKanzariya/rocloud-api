using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using ROCloud.Domain.Entities.Platform;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;
using ROCloud.Infrastructure.Persistence.Interceptors;
using ROCloud.Infrastructure.Reports;
using ROCloud.Application.Tests.Auth;

namespace ROCloud.Application.Tests.Reports;

/// <summary>
/// Integration tests for the raw-ADO.NET report repository. They run against the real
/// PostgreSQL dev database (raw ADO.NET cannot use the EF InMemory provider) and skip when
/// it is unavailable. Each test seeds a throwaway random tenant and cleans it up afterwards.
/// </summary>
public class ReportTests
{
    private const string ConnStr =
        "Host=localhost;Port=5432;Database=rocloud_dev;Username=rocloud_dev_user;Password=NjQc98y90AGe;";

    private static IConfiguration BuildConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = ConnStr })
            .Build();

    /// <summary>
    /// True when the dev PostgreSQL is reachable. Raw ADO.NET can't use the EF InMemory
    /// provider, so these tests no-op (pass) when no database is available rather than fail.
    /// </summary>
    private static bool DatabaseAvailable()
    {
        try
        {
            using var conn = new NpgsqlConnection(ConnStr);
            conn.Open();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static AppDbContext NewContext(Guid tenantId)
    {
        var ctx = new TenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnStr)
            .AddInterceptors(new TenantConnectionInterceptor(ctx, new FakeAppSettings()))
            .Options;
        return new AppDbContext(options, ctx);
    }

    private static async Task SeedTenantAsync(AppDbContext db, Guid tenantId)
    {
        var planId = await db.Plans.Select(p => p.Id).FirstAsync();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            PlanId = planId,
            Name = "Report Test Co",
            Subdomain = "rpt-" + tenantId.ToString("N")[..10],
            OwnerName = "Owner",
            OwnerEmail = $"{tenantId:N}@test.local",
            OwnerMobile = "9000000000",
            Status = TenantStatus.Active
        });
        await db.SaveChangesAsync();
    }

    private static async Task CleanupAsync(Guid tenantId)
    {
        await using var conn = new NpgsqlConnection(ConnStr);
        await conn.OpenAsync();
        await using (var setTenant = new NpgsqlCommand("SELECT set_config('app.current_tenant_id', @t, false)", conn))
        {
            setTenant.Parameters.AddWithValue("@t", tenantId.ToString());
            await setTenant.ExecuteScalarAsync();
        }
        foreach (var table in new[] { "payments", "invoices", "customers" })
        {
            await using var del = new NpgsqlCommand($"DELETE FROM {table} WHERE tenant_id = @t", conn);
            del.Parameters.AddWithValue("@t", tenantId);
            await del.ExecuteNonQueryAsync();
        }
        await using var delTenant = new NpgsqlCommand("DELETE FROM tenants WHERE id = @t", conn);
        delTenant.Parameters.AddWithValue("@t", tenantId);
        await delTenant.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task GetDailyCollection_ReturnsCorrectTotals()
    {
        if (!DatabaseAvailable()) return;
        var tenantId = Guid.NewGuid();
        await using var db = NewContext(tenantId);
        await SeedTenantAsync(db, tenantId);
        try
        {
            var customerId = Guid.NewGuid();
            db.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, Name = "C", Mobile = "1" });
            var now = DateTime.UtcNow.Date.AddHours(12);   // noon today, avoids TZ date drift
            db.Payments.Add(new Payment { Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = customerId, Amount = 100m, PaymentMethod = PaymentMethod.Cash, Status = PaymentStatus.Completed, PaidAt = now });
            db.Payments.Add(new Payment { Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = customerId, Amount = 50m, PaymentMethod = PaymentMethod.UPI, Status = PaymentStatus.Completed, PaidAt = now });
            db.Payments.Add(new Payment { Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = customerId, Amount = 999m, PaymentMethod = PaymentMethod.Cash, Status = PaymentStatus.Pending, PaidAt = now });
            await db.SaveChangesAsync();

            var repo = new ReportRepository(BuildConfig(), new FakeAppSettings());
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var result = await repo.GetDailyCollectionAsync(tenantId, today.AddDays(-1), today.AddDays(1), CancellationToken.None);

            var row = Assert.Single(result);
            Assert.Equal(150m, row.TotalCollected);   // pending 999 excluded
            Assert.Equal(100m, row.Cash);
            Assert.Equal(50m, row.Digital);
            Assert.Equal(2, row.TransactionCount);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Fact]
    public async Task GetOutstandingDues_BucketsAreCorrect()
    {
        if (!DatabaseAvailable()) return;
        var tenantId = Guid.NewGuid();
        await using var db = NewContext(tenantId);
        await SeedTenantAsync(db, tenantId);
        try
        {
            var customerId = Guid.NewGuid();
            db.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, Name = "C", Mobile = "1" });
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            Invoice Inv(decimal total, decimal paid, DateOnly due, InvoiceStatus status, int n) => new()
            {
                Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = customerId,
                InvoiceNumber = $"RPT-{n}", InvoiceDate = today, DueDate = due,
                TotalAmount = total, PaidAmount = paid, Status = status
            };
            db.Invoices.Add(Inv(100m, 0m, today, InvoiceStatus.Sent, 1));               // age 0 → 0-7
            db.Invoices.Add(Inv(200m, 50m, today.AddDays(-20), InvoiceStatus.PartiallyPaid, 2)); // bal 150, age 20 → 7-30
            db.Invoices.Add(Inv(300m, 0m, today.AddDays(-90), InvoiceStatus.Overdue, 3)); // age 90 → 60+
            db.Invoices.Add(Inv(500m, 500m, today.AddDays(-90), InvoiceStatus.Paid, 4));  // Paid → excluded
            await db.SaveChangesAsync();

            var repo = new ReportRepository(BuildConfig(), new FakeAppSettings());
            var result = await repo.GetOutstandingDuesAsync(tenantId, today, CancellationToken.None);

            var row = Assert.Single(result);
            Assert.Equal(550m, row.TotalOutstanding);   // 100 + 150 + 300
            Assert.Equal(100m, row.Bucket0To7);
            Assert.Equal(150m, row.Bucket7To30);
            Assert.Equal(0m, row.Bucket30To60);
            Assert.Equal(300m, row.Bucket60Plus);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Fact]
    public async Task ReportQuery_OtherTenantId_ReturnsEmpty()
    {
        if (!DatabaseAvailable()) return;
        var tenantId = Guid.NewGuid();
        await using var db = NewContext(tenantId);
        await SeedTenantAsync(db, tenantId);
        try
        {
            var customerId = Guid.NewGuid();
            db.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, Name = "C", Mobile = "1" });
            db.Payments.Add(new Payment { Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = customerId, Amount = 100m, PaymentMethod = PaymentMethod.Cash, Status = PaymentStatus.Completed, PaidAt = DateTime.UtcNow });
            await db.SaveChangesAsync();

            var repo = new ReportRepository(BuildConfig(), new FakeAppSettings());
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            // A different tenant id sees none of the seeded data.
            var otherTenant = await repo.GetDailyCollectionAsync(Guid.NewGuid(), today.AddDays(-1), today.AddDays(1), CancellationToken.None);
            Assert.Empty(otherTenant);

            // Sanity: the owning tenant does see it.
            var ownTenant = await repo.GetDailyCollectionAsync(tenantId, today.AddDays(-1), today.AddDays(1), CancellationToken.None);
            Assert.NotEmpty(ownTenant);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }
}
