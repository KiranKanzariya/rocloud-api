using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Models;
using ROCloud.Application.Features.Invoices.Dtos;
using ROCloud.Application.Features.Invoices.Queries.GetInvoices;
using ROCloud.Application.Features.Payments;
using ROCloud.Application.Features.Payments.Commands.CollectPayment;
using ROCloud.Application.Features.Payments.Queries.GetOutstandingDues;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;

namespace ROCloud.Application.Tests.Payments;

/// <summary>
/// A payment the owner records on the CUSTOMER page carries no invoice link. It still has to settle
/// what the customer owes — open invoices and delivered uninvoiced orders alike — oldest first, and the
/// answer is WRITTEN DOWN (InvoiceAllocationSync) so the invoice list, its status filter and the
/// reminders all read the same truth. Covers every PaymentPreference, not just Monthly.
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

    /// <summary>The customer-page modal sends no invoiceId and no orderId — that is the whole point.</summary>
    private static Task CollectOnCustomerPageAsync(AppDbContext db, TenantContext ctx, Guid customerId, decimal amount) =>
        new CollectPaymentCommandHandler(db, ctx, new FakeCurrentUser(), NullLogger<CollectPaymentCommandHandler>.Instance)
            .Handle(new CollectPaymentCommand(
                customerId, null, null, amount, nameof(PaymentMethod.Cash), null, null), CancellationToken.None);

    /// <summary>The invoice as the database now holds it — no derivation anywhere.</summary>
    private static Invoice Stored(AppDbContext db, Guid invoiceId) =>
        db.Invoices.AsNoTracking().First(i => i.Id == invoiceId);

    private static Task<decimal> BalanceAsync(AppDbContext db, Guid customerId) =>
        ROCloud.Application.Features.Customers.CustomerBalance.ComputeAsync(db, customerId, CancellationToken.None);

    [Fact]
    public async Task LumpSum_SettlesInvoicesOldestFirst_AndPartiallyPaysTheLast()
    {
        // Three invoices 400 + 400 + 300, one ₹1000 collection.
        var (db, ctx) = NewDb();
        var customerId = AddCustomer(db, PaymentPreference.Monthly);
        var inv1 = AddInvoice(db, customerId, 400m, 0);
        var inv2 = AddInvoice(db, customerId, 400m, 1);
        var inv3 = AddInvoice(db, customerId, 300m, 2);
        await db.SaveChangesAsync();

        await CollectOnCustomerPageAsync(db, ctx, customerId, 1000m);

        Assert.Equal(InvoiceStatus.Paid, Stored(db, inv1).Status);
        Assert.Equal(InvoiceStatus.Paid, Stored(db, inv2).Status);

        var third = Stored(db, inv3);
        Assert.Equal(InvoiceStatus.PartiallyPaid, third.Status);
        Assert.Equal(200m, third.PaidAmount);                       // 1000 − 800
        Assert.Equal(100m, third.TotalAmount - third.PaidAmount);
    }

    [Fact]
    public async Task LumpSum_LargerThanEverythingOwed_LeavesTheSurplusAsAdvance()
    {
        // Two invoices 400 + 400, one ₹1000 collection → both paid, ₹200 credit.
        var (db, ctx) = NewDb();
        var customerId = AddCustomer(db, PaymentPreference.Monthly);
        var inv1 = AddInvoice(db, customerId, 400m, 0);
        var inv2 = AddInvoice(db, customerId, 400m, 1);
        await db.SaveChangesAsync();

        await CollectOnCustomerPageAsync(db, ctx, customerId, 1000m);

        Assert.Equal(InvoiceStatus.Paid, Stored(db, inv1).Status);
        Assert.Equal(InvoiceStatus.Paid, Stored(db, inv2).Status);
        Assert.Equal(-200m, await BalanceAsync(db, customerId));   // a credit, not money stuck on a row
    }

    [Theory]
    [InlineData(PaymentPreference.PerBottle)]
    [InlineData(PaymentPreference.Weekly)]
    [InlineData(PaymentPreference.Monthly)]
    [InlineData(PaymentPreference.Combined)]
    public async Task OlderInvoiceIsSettledBeforeNewerOrders_ForEveryPaymentPreference(PaymentPreference preference)
    {
        // The imported-opening-balance case. Only Monthly customers are auto-invoiced, but ANY
        // preference can hold an opening invoice — so the ladder must not care which one they are.
        var (db, ctx) = NewDb();
        var customerId = AddCustomer(db, preference);
        var opening = AddInvoice(db, customerId, 450m, 0);      // 1 Jul — the imported due
        var order = AddDeliveredOrder(db, customerId, 300m, 5); // 6 Jul — a real delivery
        await db.SaveChangesAsync();

        await CollectOnCustomerPageAsync(db, ctx, customerId, 500m);

        // Oldest first: the ₹450 invoice is cleared, and only the ₹50 left over reaches the order.
        Assert.Equal(InvoiceStatus.Paid, Stored(db, opening).Status);

        var orderShare = await CustomerObligationAllocator.ComputeAsync(db, [customerId], CancellationToken.None);
        Assert.Equal(50m, orderShare.Orders[order]);

        Assert.Equal(250m, await BalanceAsync(db, customerId));   // 450 + 300 owed − 500 paid
    }

    [Fact]
    public async Task PaidInvoice_DoesNotTriggerAPaymentReminder()
    {
        // The bug this whole change exists for: the owner paid, but the reminder kept chasing them.
        var (db, ctx) = NewDb();
        var customerId = AddCustomer(db, PaymentPreference.PerBottle);
        AddInvoice(db, customerId, 450m, -30);   // long overdue
        await db.SaveChangesAsync();

        var handler = new GetOutstandingDuesQueryHandler(db);
        var before = await handler.Handle(new GetOutstandingDuesQuery(7), CancellationToken.None);
        Assert.Equal(450m, Assert.Single(before).OutstandingAmount);

        await CollectOnCustomerPageAsync(db, ctx, customerId, 450m);

        Assert.Empty(await handler.Handle(new GetOutstandingDuesQuery(7), CancellationToken.None));
    }

    [Fact]
    public async Task TheStatusFilter_FindsAnInvoiceSettledFromTheCustomerPage()
    {
        // Because the status is written down rather than derived at read time, the SQL filter agrees
        // with what is rendered. Derived, "status=Paid" missed this invoice entirely.
        var (db, ctx) = NewDb();
        var customerId = AddCustomer(db, PaymentPreference.Monthly);
        var invoiceId = AddInvoice(db, customerId, 400m, 0);
        await db.SaveChangesAsync();

        await CollectOnCustomerPageAsync(db, ctx, customerId, 400m);

        var paid = await Invoices(db, new InvoiceFilterDto { Status = nameof(InvoiceStatus.Paid) });
        Assert.Equal(invoiceId, Assert.Single(paid.Items).Id);

        var sent = await Invoices(db, new InvoiceFilterDto { Status = nameof(InvoiceStatus.Sent) });
        Assert.Empty(sent.Items);   // it is no longer Sent, and must not be returned as though it were
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

        var invoice = Stored(db, invoiceId);
        Assert.Equal(400m, invoice.PaidAmount);   // 150 linked + 250 from the pool
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);

        var orderShare = await CustomerObligationAllocator.ComputeAsync(db, [customerId], CancellationToken.None);
        Assert.Equal(50m, orderShare.Orders[order]);   // only the remainder of the 300 reaches it
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

        await new CollectPaymentCommandHandler(db, ctx, new FakeCurrentUser(), NullLogger<CollectPaymentCommandHandler>.Instance)
            .Handle(new CollectPaymentCommand(
                customerId, invoiceId, null, 500m, nameof(PaymentMethod.Cash), null, null), CancellationToken.None);

        var invoice = Stored(db, invoiceId);
        Assert.Equal(400m, invoice.PaidAmount);   // capped, not 500
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);

        var orderShare = await CustomerObligationAllocator.ComputeAsync(db, [customerId], CancellationToken.None);
        Assert.Equal(100m, orderShare.Orders[order]);   // the surplus, spent on the next thing owed
    }

    [Fact]
    public async Task Backfill_SettlesInvoicesThatPredateTheSync()
    {
        // Data written before materialisation existed: a payment sitting against the customer with no
        // link, and an invoice still marked Sent. The nightly job's recompute must put it right — this
        // is what stops legacy invoices being dunned for money already collected.
        var (db, _) = NewDb();
        var customerId = AddCustomer(db, PaymentPreference.Monthly);
        var invoiceId = AddInvoice(db, customerId, 400m, 0);
        db.Payments.Add(new Payment
        {
            Id = Guid.NewGuid(), TenantId = TenantA, CustomerId = customerId, InvoiceId = null,
            Amount = 400m, PaymentMethod = PaymentMethod.Cash, Status = PaymentStatus.Completed,
            PaidAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        Assert.Equal(InvoiceStatus.Sent, Stored(db, invoiceId).Status);   // stale, as on a live DB today

        await InvoiceAllocationSync.SyncAsync(db, customerId, CancellationToken.None);

        var invoice = Stored(db, invoiceId);
        Assert.Equal(400m, invoice.PaidAmount);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
    }

    [Fact]
    public async Task Sync_IsIdempotent_RunningItTwiceChangesNothing()
    {
        // The nightly safety net re-runs over every customer. It must never double-count.
        var (db, ctx) = NewDb();
        var customerId = AddCustomer(db, PaymentPreference.Monthly);
        var invoiceId = AddInvoice(db, customerId, 400m, 0);
        await db.SaveChangesAsync();

        await CollectOnCustomerPageAsync(db, ctx, customerId, 250m);
        var afterFirst = Stored(db, invoiceId).PaidAmount;

        await InvoiceAllocationSync.SyncAsync(db, customerId, CancellationToken.None);
        await InvoiceAllocationSync.SyncAsync(db, customerId, CancellationToken.None);

        Assert.Equal(250m, afterFirst);
        Assert.Equal(250m, Stored(db, invoiceId).PaidAmount);
        Assert.Equal(InvoiceStatus.PartiallyPaid, Stored(db, invoiceId).Status);
    }

    [Fact]
    public async Task WhenTheMoneyGoesAway_APaidInvoiceIsDemotedAgain()
    {
        // SyncAsync is a full recompute, not an increment: if the payment behind a Paid invoice is
        // reversed, the invoice must stop claiming to be paid rather than silently keeping the status.
        var (db, ctx) = NewDb();
        var customerId = AddCustomer(db, PaymentPreference.Monthly);
        var invoiceId = AddInvoice(db, customerId, 400m, 0);
        await db.SaveChangesAsync();

        await CollectOnCustomerPageAsync(db, ctx, customerId, 400m);
        Assert.Equal(InvoiceStatus.Paid, Stored(db, invoiceId).Status);

        db.Payments.RemoveRange(await db.Payments.Where(p => p.CustomerId == customerId).ToListAsync());
        await db.SaveChangesAsync();
        await InvoiceAllocationSync.SyncAsync(db, customerId, CancellationToken.None);

        var invoice = Stored(db, invoiceId);
        Assert.Equal(0m, invoice.PaidAmount);
        Assert.Equal(InvoiceStatus.Sent, invoice.Status);   // owed again
    }

    private static Task<PagedResult<InvoiceListItemDto>> Invoices(AppDbContext db, InvoiceFilterDto filter) =>
        new GetInvoicesQueryHandler(db).Handle(new GetInvoicesQuery(filter), CancellationToken.None);
}
