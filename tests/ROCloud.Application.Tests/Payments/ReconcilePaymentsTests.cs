using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Payments.Commands.ReconcilePayments;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;

namespace ROCloud.Application.Tests.Payments;

/// <summary>#3: reconciliation resolves stuck-Pending online payments and guards against double-credit.</summary>
public class ReconcilePaymentsTests
{
    private static readonly Guid TenantA = Guid.NewGuid();

    private sealed class FakeRazorpay : IRazorpayService
    {
        public Dictionary<string, RazorpayPaymentStatus> Statuses { get; } = new();
        public bool IsConfigured => true;
        public string PublicKeyId => "key";
        public string Currency => "INR";
        public Task<RazorpayOrder> CreateOrderAsync(long amountPaise, string receipt, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<RazorpayPaymentStatus> GetOrderPaymentStatusAsync(string orderId, CancellationToken ct = default)
            => Task.FromResult(Statuses.TryGetValue(orderId, out var s) ? s : new RazorpayPaymentStatus(false, null));
        public bool VerifyWebhookSignature(string rawBody, string? signature) => true;
        public Task<string> CreateSubscriptionAsync(string planId, string customerId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static AppDbContext NewDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"recon-{Guid.NewGuid()}").Options,
            new TenantContext { TenantId = TenantA });

    private static async Task<(Guid PaymentId, Guid InvoiceId)> SeedAsync(
        AppDbContext db, decimal amount, decimal total, decimal paid, InvoiceStatus invStatus, DateTime paidAt, string orderId)
    {
        var invoiceId = Guid.NewGuid();
        db.Invoices.Add(new Invoice
        {
            Id = invoiceId, TenantId = TenantA, CustomerId = Guid.NewGuid(), InvoiceNumber = "INV-1",
            TotalAmount = total, PaidAmount = paid, Status = invStatus
        });
        var paymentId = Guid.NewGuid();
        db.Payments.Add(new Payment
        {
            Id = paymentId, TenantId = TenantA, CustomerId = Guid.NewGuid(), InvoiceId = invoiceId,
            Amount = amount, PaymentMethod = PaymentMethod.Online, Status = PaymentStatus.Pending,
            ReferenceNumber = orderId, PaidAt = paidAt
        });
        await db.SaveChangesAsync();
        return (paymentId, invoiceId);
    }

    private static ReconcilePaymentsCommandHandler Handler(AppDbContext db, FakeRazorpay rp)
        => new(db, rp, NullLogger<ReconcilePaymentsCommandHandler>.Instance);

    [Fact]
    public async Task Paid_UnsettledInvoice_CompletesAndApplies()
    {
        var db = NewDb();
        var (pid, iid) = await SeedAsync(db, 500, 500, 0, InvoiceStatus.Sent, DateTime.UtcNow.AddHours(-1), "order_1");
        var rp = new FakeRazorpay(); rp.Statuses["order_1"] = new(true, "pay_1");

        var r = await Handler(db, rp).Handle(new ReconcilePaymentsCommand(), CancellationToken.None);

        Assert.Equal(1, r.Completed);
        var p = await db.Payments.FirstAsync(x => x.Id == pid);
        Assert.Equal(PaymentStatus.Completed, p.Status);
        Assert.Equal("pay_1", p.RazorpayPaymentId);
        var inv = await db.Invoices.FirstAsync(x => x.Id == iid);
        Assert.Equal(500m, inv.PaidAmount);
        Assert.Equal(InvoiceStatus.Paid, inv.Status);
    }

    [Fact]
    public async Task Paid_AlreadySettledInvoice_DoesNotDoubleCredit()
    {
        var db = NewDb();
        var (pid, iid) = await SeedAsync(db, 500, 500, 500, InvoiceStatus.Paid, DateTime.UtcNow.AddHours(-1), "order_2");
        var rp = new FakeRazorpay(); rp.Statuses["order_2"] = new(true, "pay_2");

        var r = await Handler(db, rp).Handle(new ReconcilePaymentsCommand(), CancellationToken.None);

        Assert.Equal(1, r.Duplicates);
        Assert.Equal(0, r.Completed);
        var p = await db.Payments.FirstAsync(x => x.Id == pid);
        Assert.Equal(PaymentStatus.Completed, p.Status);        // money was captured
        Assert.Contains("refund", p.Notes);                     // flagged for attention
        var inv = await db.Invoices.FirstAsync(x => x.Id == iid);
        Assert.Equal(500m, inv.PaidAmount);                     // NOT 1000 — no double-credit
    }

    [Fact]
    public async Task NotPaid_Abandoned_MarksFailed()
    {
        var db = NewDb();
        var (pid, _) = await SeedAsync(db, 500, 500, 0, InvoiceStatus.Sent, DateTime.UtcNow.AddHours(-25), "order_3");
        var r = await Handler(db, new FakeRazorpay()).Handle(new ReconcilePaymentsCommand(), CancellationToken.None);

        Assert.Equal(1, r.Failed);
        Assert.Equal(PaymentStatus.Failed, (await db.Payments.FirstAsync(x => x.Id == pid)).Status);
    }

    [Fact]
    public async Task NotPaid_StaleButRecent_LeftPending()
    {
        var db = NewDb();
        var (pid, _) = await SeedAsync(db, 500, 500, 0, InvoiceStatus.Sent, DateTime.UtcNow.AddMinutes(-30), "order_4");
        var r = await Handler(db, new FakeRazorpay()).Handle(new ReconcilePaymentsCommand(), CancellationToken.None);

        Assert.Equal(1, r.StillPending);
        Assert.Equal(PaymentStatus.Pending, (await db.Payments.FirstAsync(x => x.Id == pid)).Status);
    }

    [Fact]
    public async Task TooRecent_NotTouched()
    {
        var db = NewDb();
        var (pid, _) = await SeedAsync(db, 500, 500, 0, InvoiceStatus.Sent, DateTime.UtcNow.AddMinutes(-5), "order_5");
        var rp = new FakeRazorpay(); rp.Statuses["order_5"] = new(true, "pay_5");

        var r = await Handler(db, rp).Handle(new ReconcilePaymentsCommand(), CancellationToken.None);

        Assert.Equal(0, r.Completed + r.Failed + r.Duplicates + r.StillPending);   // below the 15-min stale window
        Assert.Equal(PaymentStatus.Pending, (await db.Payments.FirstAsync(x => x.Id == pid)).Status);
    }
}
