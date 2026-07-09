using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Payments.Dtos;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Payments.Commands.InitiateRazorpayPayment;

/// <summary>
/// Creates a Razorpay order for an online payment and a matching local Payment in Pending
/// state. Returns the checkout parameters for the Angular Razorpay widget. The payment is
/// finalised by the webhook (ConfirmRazorpayPayment).
/// </summary>
public sealed record InitiateRazorpayPaymentCommand(
    Guid CustomerId,
    Guid? InvoiceId,
    Guid? OrderId,
    decimal Amount) : IRequest<RazorpayInitiateResultDto>;

public class InitiateRazorpayPaymentCommandValidator : AbstractValidator<InitiateRazorpayPaymentCommand>
{
    public InitiateRazorpayPaymentCommandValidator()
    {
        RuleFor(c => c.CustomerId).NotEmpty();
        RuleFor(c => c.Amount).GreaterThan(0m);
    }
}

public class InitiateRazorpayPaymentCommandHandler
    : IRequestHandler<InitiateRazorpayPaymentCommand, RazorpayInitiateResultDto>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IRazorpayService _razorpay;

    public InitiateRazorpayPaymentCommandHandler(
        IAppDbContext db, ITenantContext tenant, IRazorpayService razorpay)
    {
        _db = db;
        _tenant = tenant;
        _razorpay = razorpay;
    }

    public async Task<RazorpayInitiateResultDto> Handle(
        InitiateRazorpayPaymentCommand request, CancellationToken ct)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct)
                       ?? throw new NotFoundException("Customer", request.CustomerId);

        var amountPaise = (long)Math.Round(request.Amount * 100m, MidpointRounding.AwayFromZero);
        var paymentId = Guid.NewGuid();

        var order = await _razorpay.CreateOrderAsync(amountPaise, paymentId.ToString(), ct);

        _db.Payments.Add(new Payment
        {
            Id = paymentId,
            TenantId = _tenant.TenantId,
            CustomerId = customer.Id,
            InvoiceId = request.InvoiceId,
            OrderId = request.OrderId,
            Amount = request.Amount,
            PaymentMethod = PaymentMethod.Online,
            PaymentPreference = customer.PaymentPreference,
            Status = PaymentStatus.Pending,
            ReferenceNumber = order.OrderId,   // razorpay order id; razorpay_payment_id set on confirm
            PaidAt = DateTime.UtcNow
        });

        // Non-RLS index so the anonymous webhook can resolve this order's tenant + payment.
        _db.RazorpayOrderIndexes.Add(new Domain.Entities.Platform.RazorpayOrderIndex
        {
            RazorpayOrderId = order.OrderId,
            TenantId = _tenant.TenantId,
            PaymentId = paymentId,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(ct);

        return new RazorpayInitiateResultDto(
            paymentId, order.KeyId, order.OrderId, order.AmountPaise, order.Currency);
    }
}
