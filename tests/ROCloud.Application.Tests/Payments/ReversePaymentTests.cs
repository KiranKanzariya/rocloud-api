using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Payments;
using ROCloud.Application.Features.Payments.Commands.CollectPayment;
using ROCloud.Application.Features.Payments.Commands.ReversePayment;
using ROCloud.Application.Tests.Auth;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;

namespace ROCloud.Application.Tests.Payments;

/// <summary>
/// Taking back a payment recorded by mistake.
///
/// The money-in worklist banks a customer's whole outstanding balance on one tap, which is what it is
/// for — but until this existed there was no way back from a mis-tap except editing the database.
/// </summary>
public class ReversePaymentTests
{
    private static readonly Guid TenantA = Guid.NewGuid();

    private static (AppDbContext Db, TenantContext Ctx) NewDb()
    {
        var ctx = new TenantContext { TenantId = TenantA };
        return (new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"reverse-{Guid.NewGuid()}").Options, ctx), ctx);
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

    private static ReversePaymentCommandHandler Reverser(AppDbContext db) =>
        new(db, new FakeCurrentUser { UserId = Guid.NewGuid(), TenantId = TenantA },
            NullLogger<ReversePaymentCommandHandler>.Instance);

    private static CollectPaymentCommandHandler Collector(AppDbContext db, TenantContext ctx) =>
        new(db, ctx, new FakeCurrentUser { UserId = Guid.NewGuid(), TenantId = TenantA },
            new FakeAppSettings(), NullLogger<CollectPaymentCommandHandler>.Instance);

    /// <summary>A customer with one open ₹100 invoice.</summary>
    private static async Task<(Guid CustomerId, Guid InvoiceId)> Seed(AppDbContext db)
    {
        var customerId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        db.Customers.Add(new Customer { Id = customerId, TenantId = TenantA, Name = "Ravi", Mobile = "9" });
        db.Invoices.Add(new Invoice
        {
            Id = invoiceId, TenantId = TenantA, CustomerId = customerId,
            InvoiceNumber = "INV-202608-0001",
            InvoiceDate = DateOnly.FromDateTime(DateTime.UtcNow),
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(15),
            TotalAmount = 100m, PaidAmount = 0m, Status = InvoiceStatus.Sent
        });
        await db.SaveChangesAsync();
        return (customerId, invoiceId);
    }

    [Fact]
    public async Task Reversing_UnsettlesTheInvoiceItPaid()
    {
        var (db, ctx) = NewDb();
        var (customerId, invoiceId) = await Seed(db);

        var paymentId = await Collector(db, ctx).Handle(new CollectPaymentCommand(
            customerId, invoiceId, null, 100m, nameof(PaymentMethod.Cash), null, null),
            CancellationToken.None);

        Assert.Equal(InvoiceStatus.Paid, (await db.Invoices.FirstAsync(i => i.Id == invoiceId)).Status);

        await Reverser(db).Handle(new ReversePaymentCommand(paymentId), CancellationToken.None);

        // The whole point: the money comes back off the invoice, so the customer is dunned again.
        var invoice = await db.Invoices.FirstAsync(i => i.Id == invoiceId);
        Assert.Equal(0m, invoice.PaidAmount);
        Assert.NotEqual(InvoiceStatus.Paid, invoice.Status);
    }

    [Fact]
    public async Task Reversing_MarksTheRowRatherThanDeletingIt()
    {
        var (db, ctx) = NewDb();
        var (customerId, _) = await Seed(db);

        var paymentId = await Collector(db, ctx).Handle(new CollectPaymentCommand(
            customerId, null, null, 60m, nameof(PaymentMethod.UPI), null, "Counter"),
            CancellationToken.None);

        await Reverser(db).Handle(
            new ReversePaymentCommand(paymentId, "wrong customer"), CancellationToken.None);

        // Money taken back is a fact about the day. An owner reconciling a cash box has to be able to
        // see that it happened; a row that vanished would read as the app losing a payment.
        var payment = await db.Payments.FirstOrDefaultAsync(p => p.Id == paymentId);
        Assert.NotNull(payment);
        Assert.Equal(PaymentStatus.Refunded, payment!.Status);
        Assert.Equal(60m, payment.Amount);
        Assert.Contains("Reversed: wrong customer", payment.Notes);
        // ...and whatever the collector wrote is still there.
        Assert.Contains("Counter", payment.Notes!);
    }

    [Fact]
    public async Task ReversedMoney_LeavesTheCustomersBalance()
    {
        var (db, ctx) = NewDb();
        var (customerId, _) = await Seed(db);

        var paymentId = await Collector(db, ctx).Handle(new CollectPaymentCommand(
            customerId, null, null, 100m, nameof(PaymentMethod.Cash), null, null),
            CancellationToken.None);
        await Reverser(db).Handle(new ReversePaymentCommand(paymentId), CancellationToken.None);

        // Every balance in the app sums Completed payments only — this is the filter the whole
        // reversal design rests on, so it is asserted rather than assumed.
        var counted = await db.Payments
            .Where(p => p.CustomerId == customerId && p.Status == PaymentStatus.Completed)
            .SumAsync(p => p.Amount);
        Assert.Equal(0m, counted);
    }

    [Fact]
    public async Task ReversingTwice_IsRefusedRatherThanSilentlyIgnored()
    {
        var (db, ctx) = NewDb();
        var (customerId, _) = await Seed(db);

        var paymentId = await Collector(db, ctx).Handle(new CollectPaymentCommand(
            customerId, null, null, 40m, nameof(PaymentMethod.Cash), null, null),
            CancellationToken.None);

        await Reverser(db).Handle(new ReversePaymentCommand(paymentId), CancellationToken.None);

        // Harmless to the balance — the sync is idempotent — but it would append a second note and log
        // a second reversal for money already taken back once.
        await Assert.ThrowsAsync<ValidationException>(() =>
            Reverser(db).Handle(new ReversePaymentCommand(paymentId), CancellationToken.None));
    }

    [Fact]
    public async Task AnUnknownPayment_Is404NotASilentSuccess()
    {
        var (db, _) = NewDb();
        await Assert.ThrowsAsync<NotFoundException>(() =>
            Reverser(db).Handle(new ReversePaymentCommand(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task AnotherTenantsPayment_IsNotReachable()
    {
        // Both contexts address the SAME in-memory database under different tenants — otherwise this
        // would pass for the trivial reason that tenant B's database is empty, and prove nothing about
        // the query filter it exists to test.
        var store = $"reverse-shared-{Guid.NewGuid()}";
        AppDbContext Open(TenantContext ctx) => new(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(store).Options, ctx);

        var ctxA = new TenantContext { TenantId = TenantA };
        var dbA = Open(ctxA);
        var (customerId, _) = await Seed(dbA);
        var paymentId = await Collector(dbA, ctxA).Handle(new CollectPaymentCommand(
            customerId, null, null, 25m, nameof(PaymentMethod.Cash), null, null),
            CancellationToken.None);

        var otherTenant = new TenantContext { TenantId = Guid.NewGuid() };
        var dbB = Open(otherTenant);

        // The row is right there in the store; the filter makes it a 404 rather than a 403, so the
        // caller cannot even learn it exists.
        await Assert.ThrowsAsync<NotFoundException>(() =>
            new ReversePaymentCommandHandler(
                dbB, new FakeCurrentUser { UserId = Guid.NewGuid(), TenantId = otherTenant.TenantId },
                NullLogger<ReversePaymentCommandHandler>.Instance)
                .Handle(new ReversePaymentCommand(paymentId), CancellationToken.None));

        // ...and it is still Completed afterwards, so nothing was quietly reversed cross-tenant.
        Assert.Equal(
            PaymentStatus.Completed,
            (await dbA.Payments.FirstAsync(p => p.Id == paymentId)).Status);
    }
}
