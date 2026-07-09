using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Subscription.Services;
using ROCloud.Domain.Entities.Platform;
using ROCloud.Domain.Enums;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Features.Subscription.Commands.PayInvoice;

/// <summary>
/// Marks a Pending subscription invoice Paid after payment. A paid invoice (net &gt; 0) with live
/// Razorpay must be backed by a VERIFIED order — never trust the client (guide §25). Extends the
/// tenant's subscription by one cycle (Option A) and reactivates it, and records the paid ledger row.
/// </summary>
public sealed record PayInvoiceCompleteCommand(Guid InvoiceId, string? OrderId = null) : IRequest;

public class PayInvoiceCompleteCommandHandler : IRequestHandler<PayInvoiceCompleteCommand>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IRazorpayService _razorpay;
    private readonly ISubscriptionInvoiceDelivery _invoiceDelivery;

    public PayInvoiceCompleteCommandHandler(
        IAppDbContext db, ITenantContext tenant, IRazorpayService razorpay,
        ISubscriptionInvoiceDelivery invoiceDelivery)
    {
        _db = db;
        _tenant = tenant;
        _razorpay = razorpay;
        _invoiceDelivery = invoiceDelivery;
    }

    public async Task Handle(PayInvoiceCompleteCommand request, CancellationToken ct)
    {
        var invoice = await _db.SubscriptionInvoices
            .FirstOrDefaultAsync(i => i.Id == request.InvoiceId && i.TenantId == _tenant.TenantId, ct)
            ?? throw new NotFoundException("SubscriptionInvoice", request.InvoiceId);

        if (invoice.Status != SubscriptionInvoiceStatus.Pending)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["invoice"] = ["This invoice is not open for payment."]
            });

        // Verify payment server-side for a paid invoice with live Razorpay. Free (₹0) and the
        // dev/unconfigured path skip this.
        string? paymentId = null;
        if (invoice.Amount > 0m && _razorpay.IsConfigured)
        {
            var orderId = request.OrderId ?? invoice.RazorpayOrderId;
            if (string.IsNullOrWhiteSpace(orderId))
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["payment"] = ["Payment reference is missing — complete the payment first."]
                });

            var status = await _razorpay.GetOrderPaymentStatusAsync(orderId, ct);
            if (!status.Paid)
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["payment"] = ["Payment could not be verified. The invoice was not marked paid."]
                });
            paymentId = status.PaymentId;
        }

        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == invoice.TenantId, ct)
            ?? throw new NotFoundException("Tenant", invoice.TenantId);

        // Mark the invoice paid.
        invoice.Status = SubscriptionInvoiceStatus.Paid;
        invoice.PaidAt = DateTime.UtcNow;
        invoice.RazorpayOrderId = request.OrderId ?? invoice.RazorpayOrderId;
        invoice.RazorpayPaymentId = paymentId;

        // Extend the subscription by one cycle (Option A basis rule) and reactivate.
        var yearly = string.Equals(invoice.BillingCycle, "Yearly", StringComparison.OrdinalIgnoreCase);
        var basis = tenant.SubscriptionEndsAt is { } end && end > DateTime.UtcNow ? end : DateTime.UtcNow;
        tenant.SubscriptionEndsAt = yearly ? basis.AddYears(1) : basis.AddMonths(1);
        tenant.Status = TenantStatus.Active;
        tenant.TrialEndsAt = null;

        // Paid ledger row (feeds the super-admin billing dashboard, guide §26).
        _db.PlatformBillingTransactions.Add(new PlatformBillingTransaction
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            PlanType = invoice.PlanType,
            Amount = invoice.Amount,
            BillingCycle = invoice.BillingCycle,
            Status = SubscriptionInvoiceStatus.Paid,
            RazorpayPaymentId = paymentId,
        });

        // Store the PAID PDF (sets PdfUrl) and email the owner a receipt (best-effort).
        await _invoiceDelivery.ReceiptAsync(invoice, tenant, ct);

        await _db.SaveChangesAsync(ct);
    }
}
