using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Subscription.Dtos;
using ROCloud.Application.Features.Subscription.Services;

namespace ROCloud.Application.Features.Subscription.Commands.PayInvoice;

/// <summary>
/// Begins payment of a Pending subscription invoice. Mirrors InitiateSubscription but for a specific
/// invoice: creates a Razorpay order for the invoice amount (live keys), or returns DevMode/IsFree so
/// the client can complete without the gateway. The order id is stored on the invoice.
/// </summary>
public sealed record PayInvoiceInitiateCommand(Guid InvoiceId) : IRequest<SubscriptionInitiateDto>;

public class PayInvoiceInitiateCommandHandler : IRequestHandler<PayInvoiceInitiateCommand, SubscriptionInitiateDto>
{
    private readonly IAppDbContext _db;
    private readonly IRazorpayService _razorpay;
    private readonly ITenantContext _tenant;

    public PayInvoiceInitiateCommandHandler(IAppDbContext db, IRazorpayService razorpay, ITenantContext tenant)
    {
        _db = db;
        _razorpay = razorpay;
        _tenant = tenant;
    }

    public async Task<SubscriptionInitiateDto> Handle(PayInvoiceInitiateCommand request, CancellationToken ct)
    {
        var invoice = await _db.SubscriptionInvoices
            .FirstOrDefaultAsync(i => i.Id == request.InvoiceId && i.TenantId == _tenant.TenantId, ct)
            ?? throw new NotFoundException("SubscriptionInvoice", request.InvoiceId);

        if (invoice.Status != SubscriptionInvoiceStatus.Pending)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["invoice"] = ["This invoice is not open for payment."]
            });

        var devMode = !_razorpay.IsConfigured;
        var isFree = invoice.Amount <= 0m;

        string? orderId = null;
        if (!isFree && !devMode)
        {
            var amountPaise = (long)Math.Round(invoice.Amount * 100m, MidpointRounding.AwayFromZero);
            var order = await _razorpay.CreateOrderAsync(amountPaise, invoice.InvoiceNumber, ct);
            orderId = order.OrderId;
            invoice.RazorpayOrderId = orderId;
            await _db.SaveChangesAsync(ct);
        }

        return new SubscriptionInitiateDto(
            KeyId: _razorpay.PublicKeyId,
            OrderId: orderId,
            PlanType: invoice.PlanType,
            Amount: invoice.Amount,
            Currency: _razorpay.Currency,
            DevMode: devMode,
            GrossAmount: invoice.GrossAmount,
            DiscountAmount: invoice.DiscountAmount,
            IsFree: isFree);
    }
}
