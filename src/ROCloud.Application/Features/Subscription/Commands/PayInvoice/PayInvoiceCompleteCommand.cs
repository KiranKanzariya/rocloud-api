using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Settings;
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
    private readonly IAppSettings _settings;

    public PayInvoiceCompleteCommandHandler(
        IAppDbContext db, ITenantContext tenant, IRazorpayService razorpay,
        ISubscriptionInvoiceDelivery invoiceDelivery, IAppSettings settings)
    {
        _db = db;
        _tenant = tenant;
        _razorpay = razorpay;
        _invoiceDelivery = invoiceDelivery;
        _settings = settings;
    }

    public async Task Handle(PayInvoiceCompleteCommand request, CancellationToken ct)
    {
        var invoice = await _db.SubscriptionInvoices
            .FirstOrDefaultAsync(i => i.Id == request.InvoiceId && i.TenantId == _tenant.TenantId, ct)
            ?? throw new NotFoundException("SubscriptionInvoice", request.InvoiceId);

        // Cancelled is accepted here, deliberately. The owner can be on the Razorpay screen when
        // something cancels this invoice underneath them — an admin gift, a plan change, their own
        // upgrade in another tab. Razorpay still captures the money. Refusing it then would take the
        // charge and hand back nothing, leaving a manual refund as the only remedy. A VERIFIED payment
        // is honoured whatever became of the invoice meanwhile; the unverified case is rejected below.
        if (invoice.Status == SubscriptionInvoiceStatus.Paid)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["invoice"] = ["This invoice is already paid."]
            });

        if (invoice.Status != SubscriptionInvoiceStatus.Pending
            && invoice.Status != SubscriptionInvoiceStatus.Cancelled)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["invoice"] = ["This invoice is not open for payment."]
            });

        // Verify payment server-side for a paid invoice with live Razorpay. Free (₹0) and the
        // dev/unconfigured path skip this.
        string? paymentId = null;
        string? paymentMethod = null;
        string? paymentInstrument = null;
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
            paymentMethod = status.Method;
            paymentInstrument = status.Instrument;
        }

        // A cancelled invoice is only honoured on the strength of a real captured payment. Without one
        // there is no money to protect, and paying it would resurrect a bill the platform has already
        // withdrawn — including, in dev, where verification is skipped entirely.
        if (invoice.Status == SubscriptionInvoiceStatus.Cancelled && paymentId is null)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["invoice"] = ["This invoice was cancelled. Please refresh and pay the current invoice."]
            });

        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == invoice.TenantId, ct)
            ?? throw new NotFoundException("Tenant", invoice.TenantId);

        // Mark the invoice paid. Any cancellation reason goes with it — the cancellation did not hold,
        // and a PAID invoice explaining why it was withdrawn is a contradiction on the owner's document.
        invoice.Status = SubscriptionInvoiceStatus.Paid;
        invoice.CancellationReason = null;
        invoice.PaidAt = DateTime.UtcNow;
        invoice.RazorpayOrderId = request.OrderId ?? invoice.RazorpayOrderId;
        invoice.RazorpayPaymentId = paymentId;
        invoice.PaymentMethod = paymentMethod;
        invoice.PaymentInstrument = paymentInstrument;

        // Extend by one cycle of USABLE access and reactivate — grace days are billed, locked-out days
        // are handed back (see SubscriptionTermCalculator).
        var yearly = string.Equals(invoice.BillingCycle, "Yearly", StringComparison.OrdinalIgnoreCase);
        tenant.SubscriptionEndsAt = SubscriptionTermCalculator.NextEnd(
            tenant.SubscriptionEndsAt, yearly, _settings.SubscriptionOverdueGraceDays, DateTime.UtcNow);
        // The invoice was written when the renewal fell due, assuming it would be paid on time. Restate
        // its period to the term actually granted, so the document the owner keeps is not contradicted
        // by the access they receive.
        invoice.PeriodEnd = DateOnly.FromDateTime(tenant.SubscriptionEndsAt.Value);
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
            PaymentMethod = paymentMethod,
            PaymentInstrument = paymentInstrument,
            SubscriptionInvoiceId = invoice.Id,   // link the ledger row to the invoice it paid
        });

        // Store the PAID PDF (sets PdfUrl) and email the owner a receipt (best-effort).
        await _invoiceDelivery.ReceiptAsync(invoice, tenant, ct);

        await _db.SaveChangesAsync(ct);
    }
}
