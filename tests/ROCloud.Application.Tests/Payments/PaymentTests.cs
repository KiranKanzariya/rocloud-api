using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Payments.Commands.CollectPayment;
using ROCloud.Application.Features.Payments.Commands.ConfirmRazorpayPayment;
using ROCloud.Domain.Entities.Platform;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.ExternalServices;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;

namespace ROCloud.Application.Tests.Payments;

public class PaymentTests
{
    private const string WebhookSecret = "whsec_test_123";
    private static readonly Guid TenantA = Guid.NewGuid();

    private static (AppDbContext Db, TenantContext Ctx) NewDb()
    {
        var ctx = new TenantContext { TenantId = TenantA };
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"payments-{Guid.NewGuid()}").Options, ctx);
        return (db, ctx);
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

    private static RazorpayService NewRazorpay()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Razorpay:KeyId"] = "rzp_test_abc",
                ["Razorpay:KeySecret"] = "secret_abc",
                ["Razorpay:WebhookSecret"] = WebhookSecret
            })
            .Build();
        return new RazorpayService(new HttpClient(), config, NullLogger<RazorpayService>.Instance);
    }

    private static string Sign(string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(WebhookSecret));
        return Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(body)));
    }

    [Fact]
    public async Task CollectPayment_UpdatesInvoiceStatus()
    {
        var (db, ctx) = NewDb();
        var customerId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        db.Customers.Add(new Customer { Id = customerId, TenantId = TenantA, Name = "Ravi", Mobile = "9" });
        db.Invoices.Add(new Invoice
        {
            Id = invoiceId, TenantId = TenantA, CustomerId = customerId,
            InvoiceNumber = "INV-202606-0001", InvoiceDate = DateOnly.FromDateTime(DateTime.UtcNow),
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(15),
            TotalAmount = 100m, PaidAmount = 0m, Status = InvoiceStatus.Sent
        });
        await db.SaveChangesAsync();

        var handler = new CollectPaymentCommandHandler(
            db, ctx, new FakeCurrentUser { UserId = Guid.NewGuid(), TenantId = TenantA },
            NullLogger<CollectPaymentCommandHandler>.Instance);

        // Partial payment → PartiallyPaid.
        await handler.Handle(new CollectPaymentCommand(
            customerId, invoiceId, null, 40m, nameof(PaymentMethod.Cash), null, null), CancellationToken.None);
        var afterPartial = await db.Invoices.FirstAsync(i => i.Id == invoiceId);
        Assert.Equal(40m, afterPartial.PaidAmount);
        Assert.Equal(InvoiceStatus.PartiallyPaid, afterPartial.Status);

        // Remaining payment → Paid.
        await handler.Handle(new CollectPaymentCommand(
            customerId, invoiceId, null, 60m, nameof(PaymentMethod.UPI), null, null), CancellationToken.None);
        var afterFull = await db.Invoices.FirstAsync(i => i.Id == invoiceId);
        Assert.Equal(100m, afterFull.PaidAmount);
        Assert.Equal(InvoiceStatus.Paid, afterFull.Status);
    }

    [Fact]
    public async Task RazorpayWebhook_InvalidSignature_Throws403()
    {
        var (db, ctx) = NewDb();
        var handler = new ConfirmRazorpayPaymentCommandHandler(
            db, ctx, NewRazorpay(), NullLogger<ConfirmRazorpayPaymentCommandHandler>.Instance);

        var body = "{\"event\":\"payment.captured\"}";
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => handler.Handle(
            new ConfirmRazorpayPaymentCommand(body, "deadbeef", "order_x", "pay_x"), CancellationToken.None));
    }

    [Fact]
    public async Task RazorpayWebhook_ValidSignature_MarksPaymentComplete()
    {
        var (db, ctx) = NewDb();
        var customerId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        const string orderId = "order_ABC123";

        db.Customers.Add(new Customer { Id = customerId, TenantId = TenantA, Name = "Ravi", Mobile = "9" });
        db.Invoices.Add(new Invoice
        {
            Id = invoiceId, TenantId = TenantA, CustomerId = customerId,
            InvoiceNumber = "INV-202606-0002", InvoiceDate = DateOnly.FromDateTime(DateTime.UtcNow),
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(15),
            TotalAmount = 250m, PaidAmount = 0m, Status = InvoiceStatus.Sent
        });
        db.Payments.Add(new Payment
        {
            Id = paymentId, TenantId = TenantA, CustomerId = customerId, InvoiceId = invoiceId,
            Amount = 250m, PaymentMethod = PaymentMethod.Online, Status = PaymentStatus.Pending,
            ReferenceNumber = orderId, PaidAt = DateTime.UtcNow
        });
        db.RazorpayOrderIndexes.Add(new RazorpayOrderIndex
        {
            RazorpayOrderId = orderId, TenantId = TenantA, PaymentId = paymentId, CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        // Simulate the anonymous webhook: no tenant context — the handler must resolve it from the index.
        ctx.TenantId = Guid.Empty;

        var body = $"{{\"payload\":{{\"payment\":{{\"entity\":{{\"order_id\":\"{orderId}\",\"id\":\"pay_XYZ\"}}}}}}}}";
        var handler = new ConfirmRazorpayPaymentCommandHandler(
            db, ctx, NewRazorpay(), NullLogger<ConfirmRazorpayPaymentCommandHandler>.Instance);

        await handler.Handle(
            new ConfirmRazorpayPaymentCommand(body, Sign(body), orderId, "pay_XYZ"), CancellationToken.None);

        var payment = await db.Payments.FirstAsync(p => p.Id == paymentId);
        Assert.Equal(PaymentStatus.Completed, payment.Status);
        Assert.Equal("pay_XYZ", payment.RazorpayPaymentId);

        var invoice = await db.Invoices.FirstAsync(i => i.Id == invoiceId);
        Assert.Equal(250m, invoice.PaidAmount);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
    }
}
