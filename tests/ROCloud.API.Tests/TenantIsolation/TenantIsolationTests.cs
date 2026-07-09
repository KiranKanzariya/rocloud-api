using Microsoft.EntityFrameworkCore;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;

namespace ROCloud.API.Tests.TenantIsolation;

/// <summary>
/// Verifies the EF Core global query filter isolates tenants for the sensitive entities
/// (customers, orders, invoices, payments). This is the primary isolation mechanism;
/// PostgreSQL RLS adds defence-in-depth when running against a real database.
///
/// Entities are seeded with Add() (which bypasses the filter), then read back through the
/// filter while the context is scoped to tenant A.
/// </summary>
public class TenantIsolationTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static AppDbContext NewDbForTenantA() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"isolation-{Guid.NewGuid()}").Options,
            new TenantContext { TenantId = TenantA });

    private static Customer NewCustomer(Guid tenantId) =>
        new() { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Customer", Mobile = "9000000000" };

    private static Order NewOrder(Guid tenantId) =>
        new() { Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = Guid.NewGuid(), OrderDate = Today };

    private static Invoice NewInvoice(Guid tenantId) =>
        new() { Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = Guid.NewGuid(), InvoiceNumber = "INV-1", InvoiceDate = Today, DueDate = Today };

    private static Payment NewPayment(Guid tenantId) =>
        new() { Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = Guid.NewGuid(), Amount = 100m, PaymentMethod = PaymentMethod.Cash, PaidAt = DateTime.UtcNow };

    // ── Customers ─────────────────────────────────────────────────────────
    [Fact]
    public async Task GetCustomers_AsUserOfTenantA_DoesNotReturnTenantBData()
    {
        await using var db = NewDbForTenantA();
        db.Customers.Add(NewCustomer(TenantA));
        db.Customers.Add(NewCustomer(TenantB));
        await db.SaveChangesAsync();

        var customers = await db.Customers.ToListAsync();

        Assert.All(customers, c => Assert.Equal(TenantA, c.TenantId));
        Assert.Single(customers);
    }

    [Fact]
    public async Task GetCustomerById_FromOtherTenant_ReturnsNull()
    {
        await using var db = NewDbForTenantA();
        var other = NewCustomer(TenantB);
        db.Customers.Add(other);
        await db.SaveChangesAsync();

        var found = await db.Customers.FirstOrDefaultAsync(c => c.Id == other.Id);

        Assert.Null(found);
    }

    // ── Orders ────────────────────────────────────────────────────────────
    [Fact]
    public async Task GetOrders_AsUserOfTenantA_DoesNotReturnTenantBData()
    {
        await using var db = NewDbForTenantA();
        db.Orders.Add(NewOrder(TenantA));
        db.Orders.Add(NewOrder(TenantB));
        await db.SaveChangesAsync();

        var orders = await db.Orders.ToListAsync();

        Assert.All(orders, o => Assert.Equal(TenantA, o.TenantId));
        Assert.Single(orders);
    }

    [Fact]
    public async Task GetOrderById_FromOtherTenant_ReturnsNull()
    {
        await using var db = NewDbForTenantA();
        var other = NewOrder(TenantB);
        db.Orders.Add(other);
        await db.SaveChangesAsync();

        Assert.Null(await db.Orders.FirstOrDefaultAsync(o => o.Id == other.Id));
    }

    // ── Invoices ──────────────────────────────────────────────────────────
    [Fact]
    public async Task GetInvoices_AsUserOfTenantA_DoesNotReturnTenantBData()
    {
        await using var db = NewDbForTenantA();
        db.Invoices.Add(NewInvoice(TenantA));
        db.Invoices.Add(NewInvoice(TenantB));
        await db.SaveChangesAsync();

        var invoices = await db.Invoices.ToListAsync();

        Assert.All(invoices, i => Assert.Equal(TenantA, i.TenantId));
        Assert.Single(invoices);
    }

    [Fact]
    public async Task GetInvoiceById_FromOtherTenant_ReturnsNull()
    {
        await using var db = NewDbForTenantA();
        var other = NewInvoice(TenantB);
        db.Invoices.Add(other);
        await db.SaveChangesAsync();

        Assert.Null(await db.Invoices.FirstOrDefaultAsync(i => i.Id == other.Id));
    }

    // ── Payments ──────────────────────────────────────────────────────────
    [Fact]
    public async Task GetPayments_AsUserOfTenantA_DoesNotReturnTenantBData()
    {
        await using var db = NewDbForTenantA();
        db.Payments.Add(NewPayment(TenantA));
        db.Payments.Add(NewPayment(TenantB));
        await db.SaveChangesAsync();

        var payments = await db.Payments.ToListAsync();

        Assert.All(payments, p => Assert.Equal(TenantA, p.TenantId));
        Assert.Single(payments);
    }

    [Fact]
    public async Task GetPaymentById_FromOtherTenant_ReturnsNull()
    {
        await using var db = NewDbForTenantA();
        var other = NewPayment(TenantB);
        db.Payments.Add(other);
        await db.SaveChangesAsync();

        Assert.Null(await db.Payments.FirstOrDefaultAsync(p => p.Id == other.Id));
    }
}
