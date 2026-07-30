using ROCloud.Application.Common;
using ROCloud.Application.Features.Payments.Queries.GetOutstandingDues;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ROCloud.Application.Tests.Payments;

/// <summary>
/// Outstanding = the customer ledger (billed − paid), aged past the overdue window — so a per-bottle /
/// weekly customer's UNINVOICED delivered dues count too. The old invoice-only definition reported them
/// as ₹0 while they genuinely owed.
/// </summary>
public class OutstandingDuesTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly DateOnly Today = AppTimeZone.Today(DateTime.UtcNow);
    private static readonly DateOnly Aged = Today.AddDays(-10);    // past the 7-day overdue cutoff
    private static readonly DateOnly Recent = Today.AddDays(-2);   // inside the cutoff

    private static AppDbContext NewDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"od-{Guid.NewGuid()}").Options,
            new TenantContext { TenantId = TenantA });

    private static Guid AddCustomer(AppDbContext db, string name)
    {
        var id = Guid.NewGuid();
        db.Customers.Add(new Customer { Id = id, TenantId = TenantA, Name = name, Mobile = "9" });
        return id;
    }

    /// <summary>An uninvoiced delivered order for `amount` (qty×rate) on `date`.</summary>
    private static void AddDeliveredOrder(AppDbContext db, Guid customerId, DateOnly date, decimal amount)
    {
        var orderId = Guid.NewGuid();
        db.Orders.Add(new Order
        {
            Id = orderId, TenantId = TenantA, CustomerId = customerId,
            OrderDate = date, Status = OrderStatus.Delivered
        });
        db.OrderItems.Add(new OrderItem
        {
            Id = Guid.NewGuid(), TenantId = TenantA, OrderId = orderId, ProductId = Guid.NewGuid(),
            Quantity = 1, UnitRate = amount
        });
    }

    [Fact]
    public async Task IncludesAPerBottleCustomersUninvoicedDeliveredDues()
    {
        var db = NewDb();
        var perBottle = AddCustomer(db, "Per Bottle");   // no invoices — dues live on delivered orders
        AddDeliveredOrder(db, perBottle, Aged, 150m);
        await db.SaveChangesAsync();

        var rows = await new GetOutstandingDuesQueryHandler(db).Handle(new GetOutstandingDuesQuery(7), CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal(perBottle, row.CustomerId);
        Assert.Equal(150m, row.OutstandingAmount);   // was ₹0 under the invoice-only definition
        Assert.Equal(Today.DayNumber - Aged.DayNumber, row.DaysOverdue);
    }

    [Fact]
    public async Task ExcludesACustomerWhoseDeliveredDuesArePaidOff()
    {
        var db = NewDb();
        var settled = AddCustomer(db, "Settled");
        AddDeliveredOrder(db, settled, Aged, 100m);
        db.Payments.Add(new Payment
        {
            Id = Guid.NewGuid(), TenantId = TenantA, CustomerId = settled,
            Amount = 100m, PaymentMethod = PaymentMethod.Cash, Status = PaymentStatus.Completed,
            PaidAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var rows = await new GetOutstandingDuesQueryHandler(db).Handle(new GetOutstandingDuesQuery(7), CancellationToken.None);

        Assert.Empty(rows);   // billed 100 − paid 100 = 0
    }

    [Fact]
    public async Task ExcludesRecentDues_OnlyChasesWhatIsAged()
    {
        var db = NewDb();
        var recent = AddCustomer(db, "Recent");
        AddDeliveredOrder(db, recent, Recent, 80m);   // delivered 2 days ago — not yet overdue
        await db.SaveChangesAsync();

        var rows = await new GetOutstandingDuesQueryHandler(db).Handle(new GetOutstandingDuesQuery(7), CancellationToken.None);

        Assert.Empty(rows);
    }

    [Fact]
    public async Task StillCountsAnAgedUnpaidInvoice()
    {
        var db = NewDb();
        var invoiced = AddCustomer(db, "Invoiced");
        db.Invoices.Add(new Invoice
        {
            Id = Guid.NewGuid(), TenantId = TenantA, CustomerId = invoiced, InvoiceNumber = "INV-1",
            TotalAmount = 200m, PaidAmount = 0m, Status = InvoiceStatus.Sent, DueDate = Aged
        });
        await db.SaveChangesAsync();

        var rows = await new GetOutstandingDuesQueryHandler(db).Handle(new GetOutstandingDuesQuery(7), CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal(200m, row.OutstandingAmount);
        Assert.Equal(1, row.InvoiceCount);
    }
}
