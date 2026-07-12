using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Invoices;
using ROCloud.Application.Features.Payments;
using ROCloud.Application.Features.Payments.Commands.CollectPayment;
using ROCloud.Application.Features.Payments.Queries.GetOutstandingDues;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;

namespace ROCloud.Application.Tests.Payments;

/// <summary>
/// A payment the owner records on the CUSTOMER page carries no invoice/order link. It still has to
/// settle what the customer owes — open invoices and delivered uninvoiced orders alike — oldest first.
/// These cover the money maths for every PaymentPreference, not just Monthly.
/// </summary>
public class ObligationAllocationTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly DateOnly Day1 = new(2026, 7, 1);

    private static (AppDbContext Db, TenantContext Ctx) NewDb()
    {
        var ctx = new TenantContext { TenantId = TenantA };
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"obligations-{Guid.NewGuid()}").Options, ctx);
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

    private static Guid AddCustomer(AppDbContext db, PaymentPreference preference)
    {
        var id = Guid.NewGuid();
        db.Customers.Add(new Customer
        {
            Id = id, TenantId = TenantA, Name = $"C-{id:N}", Mobile = "+919876543210",
            Email = "c@example.com", PaymentPreference = preference, IsActive = true
        });
        return id;
    }

    /// <summary>An invoice raised on <paramref name="dayOffset"/>, due the same day (like an opening balance).</summary>
    private static Guid AddInvoice(
        AppDbContext db, Guid customerId, decimal total, int dayOffset,
        decimal paid = 0m, InvoiceStatus status = InvoiceStatus.Sent)
    {
        var id = Guid.NewGuid();
        var date = Day1.AddDays(dayOffset);
        db.Invoices.Add(new Invoice
        {
            Id = id, TenantId = TenantA, CustomerId = customerId,
            InvoiceNumber = $"INV-{id:N}"[..16], InvoiceDate = date, DueDate = date,
            SubTotal = total, TotalAmount = total, PaidAmount = paid, Status = status,
            CreatedAt = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
        });
        return id;
    }

    private static Guid AddDeliveredOrder(AppDbContext db, Guid customerId, decimal total, int dayOffset)
    {
        var id = Guid.NewGuid();
        var date = Day1.AddDays(dayOffset);
        db.Orders.Add(new Order
        {
            Id = id, TenantId = TenantA, CustomerId = customerId, OrderDate = date,
            Status = OrderStatus.Delivered,
            CreatedAt = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
        });
        db.OrderItems.Add(new OrderItem
        {
            Id = Guid.NewGuid(), TenantId = TenantA, OrderId = id, ProductId = Guid.NewGuid(),
            Quantity = 1, UnitRate = total
        });
        return id;
    }

    private static async Task CollectOnCustomerPageAsync(AppDbContext db, TenantContext ctx, Guid customerId, decimal amount)
    {
        var handler = new CollectPaymentCommandHandler(
            db, ctx, new FakeCurrentUser(), NullLogger<CollectPaymentCommandHandler>.Instance);
        // The customer-page modal sends no invoiceId and no orderId — that is the whole point.
        await handler.Handle(new CollectPaymentCommand(
            customerId, null, null, amount, nameof(PaymentMethod.Cash), null, null), CancellationToken.None);
    }

    [Fact]
    public async Task LumpSum_SettlesInvoicesOldestFirst_AndPartiallyPaysTheLast()
    {
        // Scenario 1: three invoices 400 + 400 + 300, one ₹1000 collection.
        var (db, ctx) = NewDb();
        var customerId = AddCustomer(db, PaymentPreference.Monthly);
        var inv1 = AddInvoice(db, customerId, 400m, 0);
        var inv2 = AddInvoice(db, customerId, 400m, 1);
        var inv3 = AddInvoice(db, customerId, 300m, 2);
        await db.SaveChangesAsync();

        await CollectOnCustomerPageAsync(db, ctx, customerId, 1000m);

        var applied = (await CustomerObligationAllocator.ComputeAsync(db, [customerId], CancellationToken.None)).Invoices;
        Assert.Equal(400m, applied[inv1]);
        Assert.Equal(400m, applied[inv2]);
        Assert.Equal(200m, applied[inv3]);   // 1000 − 800

        Assert.Equal(InvoiceStatus.Paid, Resolve(db, inv1, applied).Status);
        Assert.Equal(InvoiceStatus.Paid, Resolve(db, inv2, applied).Status);

        var third = Resolve(db, inv3, applied);
        Assert.Equal(InvoiceStatus.PartiallyPaid, third.Status);
        Assert.Equal(100m, third.Balance);
    }

    [Fact]
    public async Task LumpSum_LargerThanEverythingOwed_LeavesTheSurplusAsAdvance()
    {
        // Scenario 2: two invoices 400 + 400, one ₹1000 collection → both paid, ₹200 credit.
        var (db, ctx) = NewDb();
        var customerId = AddCustomer(db, PaymentPreference.Monthly);
        var inv1 = AddInvoice(db, customerId, 400m, 0);
        var inv2 = AddInvoice(db, customerId, 400m, 1);
        await db.SaveChangesAsync();

        await CollectOnCustomerPageAsync(db, ctx, customerId, 1000m);

        var applied = (await CustomerObligationAllocator.ComputeAsync(db, [customerId], CancellationToken.None)).Invoices;
        Assert.Equal(InvoiceStatus.Paid, Resolve(db, inv1, applied).Status);
        Assert.Equal(InvoiceStatus.Paid, Resolve(db, inv2, applied).Status);

        // The ₹200 surplus is a credit, not money stuck on an invoice.
        var balance = await ROCloud.Application.Features.Customers.CustomerBalance.ComputeAsync(db, customerId, CancellationToken.None);
        Assert.Equal(-200m, balance);
    }

    [Theory]
    [InlineData(PaymentPreference.PerBottle)]
    [InlineData(PaymentPreference.Weekly)]
    [InlineData(PaymentPreference.Monthly)]
    [InlineData(PaymentPreference.Combined)]
    public async Task OlderInvoiceIsSettledBeforeNewerOrders_ForEveryPaymentPreference(PaymentPreference preference)
    {
        // The imported-opening-balance case: an old invoice plus later deliveries. Only Monthly customers
        // are auto-invoiced, but ANY preference can hold an opening invoice — so the ladder must not care.
        var (db, ctx) = NewDb();
        var customerId = AddCustomer(db, preference);
        var opening = AddInvoice(db, customerId, 450m, 0);      // 1 Jul — the imported due
        var order = AddDeliveredOrder(db, customerId, 300m, 5); // 6 Jul — a real delivery
        await db.SaveChangesAsync();

        await CollectOnCustomerPageAsync(db, ctx, customerId, 500m);

        var result = await CustomerObligationAllocator.ComputeAsync(db, [customerId], CancellationToken.None);

        // Oldest first: the ₹450 invoice is cleared, and only the ₹50 left over reaches the order.
        Assert.Equal(450m, result.Invoices[opening]);
        Assert.Equal(50m, result.Orders[order]);
        Assert.Equal(InvoiceStatus.Paid, Resolve(db, opening, result.Invoices).Status);

        // 450 + 300 owed − 500 paid = 250 still due.
        var balance = await ROCloud.Application.Features.Customers.CustomerBalance.ComputeAsync(db, customerId, CancellationToken.None);
        Assert.Equal(250m, balance);
    }

    [Fact]
    public async Task PaidInvoice_DoesNotTriggerAPaymentReminder()
    {
        // The bug this whole change exists for: the owner paid, but the reminder kept chasing them
        // because the payment was never linked to the invoice row.
        var (db, ctx) = NewDb();
        var customerId = AddCustomer(db, PaymentPreference.PerBottle);
        AddInvoice(db, customerId, 450m, -30);   // long overdue
        await db.SaveChangesAsync();

        var handler = new GetOutstandingDuesQueryHandler(db);
        var before = await handler.Handle(new GetOutstandingDuesQuery(7), CancellationToken.None);
        Assert.Equal(450m, Assert.Single(before).OutstandingAmount);

        await CollectOnCustomerPageAsync(db, ctx, customerId, 450m);

        var after = await handler.Handle(new GetOutstandingDuesQuery(7), CancellationToken.None);
        Assert.Empty(after);
    }

    [Fact]
    public async Task PartlyPaidInvoice_ReEntersTheLadderAtItsRemainder()
    {
        // ₹150 was recorded against the invoice itself; the pool must only owe the other ₹250, and the
        // linked ₹150 must not also be spent on the order.
        var (db, ctx) = NewDb();
        var customerId = AddCustomer(db, PaymentPreference.Combined);
        var invoiceId = AddInvoice(db, customerId, 400m, 0, paid: 150m, status: InvoiceStatus.PartiallyPaid);
        var order = AddDeliveredOrder(db, customerId, 200m, 3);
        db.Payments.Add(new Payment
        {
            Id = Guid.NewGuid(), TenantId = TenantA, CustomerId = customerId, InvoiceId = invoiceId,
            Amount = 150m, PaymentMethod = PaymentMethod.Cash, Status = PaymentStatus.Completed,
            PaidAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        await CollectOnCustomerPageAsync(db, ctx, customerId, 300m);

        var result = await CustomerObligationAllocator.ComputeAsync(db, [customerId], CancellationToken.None);
        Assert.Equal(250m, result.Invoices[invoiceId]);  // 400 − 150 already recorded
        Assert.Equal(50m, result.Orders[order]);         // only the remainder of the 300 reaches it

        var resolved = Resolve(db, invoiceId, result.Invoices);
        Assert.Equal(400m, resolved.PaidAmount);
        Assert.Equal(InvoiceStatus.Paid, resolved.Status);
    }

    [Fact]
    public async Task OverpayingOneInvoice_ReleasesTheSurplusToTheCustomersOtherDues()
    {
        // Paying ₹500 against a ₹400 invoice used to inflate PaidAmount past the total and strand the
        // ₹100. It is now capped, so the surplus falls into the pool and settles the next obligation.
        var (db, ctx) = NewDb();
        var customerId = AddCustomer(db, PaymentPreference.Monthly);
        var invoiceId = AddInvoice(db, customerId, 400m, 0);
        var order = AddDeliveredOrder(db, customerId, 200m, 2);
        await db.SaveChangesAsync();

        var handler = new CollectPaymentCommandHandler(
            db, ctx, new FakeCurrentUser(), NullLogger<CollectPaymentCommandHandler>.Instance);
        await handler.Handle(new CollectPaymentCommand(
            customerId, invoiceId, null, 500m, nameof(PaymentMethod.Cash), null, null), CancellationToken.None);

        var invoice = await db.Invoices.FirstAsync(i => i.Id == invoiceId);
        Assert.Equal(400m, invoice.PaidAmount);   // capped, not 500
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);

        var result = await CustomerObligationAllocator.ComputeAsync(db, [customerId], CancellationToken.None);
        Assert.Equal(100m, result.Orders[order]); // the surplus, spent on the next thing owed
    }

    /// <summary>The invoice as the owner sees it: recorded payments plus its share of the pool.</summary>
    private static (decimal PaidAmount, decimal Balance, InvoiceStatus Status) Resolve(
        AppDbContext db, Guid invoiceId, IReadOnlyDictionary<Guid, decimal> applied)
    {
        var i = db.Invoices.AsNoTracking().First(x => x.Id == invoiceId);
        return InvoicePaymentStatus.Resolve(i.Status, i.TotalAmount, i.PaidAmount, applied.GetValueOrDefault(invoiceId, 0m));
    }
}
