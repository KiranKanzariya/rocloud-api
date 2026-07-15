using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Payments.Commands.ConfirmRazorpayPayment;

/// <summary>
/// Razorpay webhook handler. Verifies the HMAC signature against the raw body (NOT just the route
/// attribute). Anonymous, so it has no tenant context — it resolves the tenant from the NON-RLS
/// razorpay_order_index, enters that tenant's context, then reads the RLS-protected payment (a
/// cross-tenant read with IgnoreQueryFilters would still return nothing — RLS applies). Marks the
/// payment Completed and credits the invoice, unless it's already settled (double-credit guard,
/// matching reconciliation). Idempotent.
/// </summary>
public sealed record ConfirmRazorpayPaymentCommand(
    string RawBody,
    string? Signature,
    string RazorpayOrderId,
    string RazorpayPaymentId) : IRequest;

public class ConfirmRazorpayPaymentCommandHandler : IRequestHandler<ConfirmRazorpayPaymentCommand>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IRazorpayService _razorpay;
    private readonly ILogger<ConfirmRazorpayPaymentCommandHandler> _logger;

    public ConfirmRazorpayPaymentCommandHandler(
        IAppDbContext db, ITenantContext tenant, IRazorpayService razorpay,
        ILogger<ConfirmRazorpayPaymentCommandHandler> logger)
    {
        _db = db;
        _tenant = tenant;
        _razorpay = razorpay;
        _logger = logger;
    }

    public async Task Handle(ConfirmRazorpayPaymentCommand request, CancellationToken ct)
    {
        if (!_razorpay.VerifyWebhookSignature(request.RawBody, request.Signature))
        {
            _logger.LogWarning("Razorpay webhook rejected: invalid signature for order {OrderId}", request.RazorpayOrderId);
            throw new ForbiddenAccessException("Invalid webhook signature.");
        }

        // Resolve the tenant from the non-RLS index (readable without any tenant context)...
        var index = await _db.RazorpayOrderIndexes
            .FirstOrDefaultAsync(x => x.RazorpayOrderId == request.RazorpayOrderId, ct);
        if (index is null)
        {
            _logger.LogWarning("Razorpay webhook: no order index for {OrderId}", request.RazorpayOrderId);
            return;   // ack so Razorpay stops retrying an unknown order
        }

        // ...then enter that tenant's context so the RLS-protected payment read succeeds. The tenant
        // connection interceptor re-applies the tenant GUC on the next connection open.
        _tenant.TenantId = index.TenantId;

        var payment = await _db.Payments.FirstOrDefaultAsync(p => p.Id == index.PaymentId, ct);
        if (payment is null)
        {
            _logger.LogWarning("Razorpay webhook: indexed payment {PaymentId} not found", index.PaymentId);
            return;
        }

        if (payment.Status == PaymentStatus.Completed)
            return;   // idempotent — webhook deliveries can repeat (and reconciliation may have run)

        payment.Status = PaymentStatus.Completed;
        payment.RazorpayPaymentId = request.RazorpayPaymentId;
        payment.PaidAt = DateTime.UtcNow;

        if (payment.InvoiceId is { } invoiceId)
        {
            var invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId, ct);
            if (invoice is not null)
            {
                // Double-credit guard: don't re-credit an invoice already settled (e.g. by cash).
                var alreadySettled = invoice.Status == InvoiceStatus.Paid
                                     || invoice.PaidAmount >= invoice.TotalAmount;
                if (!alreadySettled)
                    Payments.PaymentApplication.ApplyToInvoice(invoice, payment.Amount);
                else
                    payment.Notes = "Invoice already settled — possible duplicate payment, refund may be due.";
            }
        }

        await _db.SaveChangesAsync(ct);

        // The money only becomes real now (the row was Pending until this point), so re-settle the
        // customer's invoices against it.
        await InvoiceAllocationSync.SyncAsync(_db, payment.CustomerId, ct);

        _logger.LogInformation(
            "Razorpay payment {RpPaymentId} confirmed for local payment {PaymentId}",
            request.RazorpayPaymentId, payment.Id);
    }
}
