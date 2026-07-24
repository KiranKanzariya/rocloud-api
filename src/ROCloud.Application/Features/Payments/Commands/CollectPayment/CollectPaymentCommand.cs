using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Features.Payments.Commands.CollectPayment;

/// <summary>
/// Records a manual cash/UPI/etc. payment. May link to an invoice, an order, or neither
/// (advance payment). When linked to an invoice, updates its paid amount and status.
/// </summary>
public sealed record CollectPaymentCommand(
    Guid CustomerId,
    Guid? InvoiceId,
    Guid? OrderId,
    decimal Amount,
    string PaymentMethod,
    string? ReferenceNumber,
    string? Notes) : IRequest<Guid>;

public class CollectPaymentCommandValidator : AbstractValidator<CollectPaymentCommand>
{
    public CollectPaymentCommandValidator()
    {
        RuleFor(c => c.CustomerId).NotEmpty();
        RuleFor(c => c.Amount).GreaterThan(0m);
        RuleFor(c => c.PaymentMethod)
            .Must(v => Enum.GetNames<PaymentMethod>().Contains(v) && v != nameof(Domain.Enums.PaymentMethod.None))
            .WithMessage("Invalid payment method.");
        RuleFor(c => c.ReferenceNumber).MaximumLength(100);
    }
}

public class CollectPaymentCommandHandler : IRequestHandler<CollectPaymentCommand, Guid>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<CollectPaymentCommandHandler> _logger;

    public CollectPaymentCommandHandler(
        IAppDbContext db, ITenantContext tenant, ICurrentUserService currentUser,
        ILogger<CollectPaymentCommandHandler> logger)
    {
        _db = db;
        _tenant = tenant;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<Guid> Handle(CollectPaymentCommand request, CancellationToken ct)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct)
                       ?? throw new NotFoundException("Customer", request.CustomerId);

        Invoice? invoice = null;
        if (request.InvoiceId is { } invoiceId)
        {
            invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
                      ?? throw new NotFoundException("Invoice", invoiceId);
            if (invoice.CustomerId != customer.Id)
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["invoiceId"] = ["The invoice does not belong to this customer."]
                });
        }

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            CustomerId = customer.Id,
            InvoiceId = request.InvoiceId,
            OrderId = request.OrderId,
            Amount = request.Amount,
            PaymentMethod = Enum.Parse<PaymentMethod>(request.PaymentMethod),
            PaymentPreference = customer.PaymentPreference,
            Status = PaymentStatus.Completed,
            ReferenceNumber = request.ReferenceNumber,
            CollectedBy = _currentUser.UserId,
            PaidAt = DateTime.UtcNow,
            Notes = request.Notes
        };
        _db.Payments.Add(payment);

        if (invoice is not null)
            PaymentApplication.ApplyToInvoice(invoice, request.Amount);

        // The payment row and the invoice settlement it causes are one fact, so they commit together.
        // Without this the payment saves, and a failure in the allocation sync below leaves the money
        // banked while the invoice still reads unpaid — the customer keeps getting dunned for it until
        // the nightly InvoiceAllocationSyncJob catches up. The in-memory test provider is
        // non-relational and has no transactions, hence the IsRelational guard (same as ImportCustomers).
        await using var tx = _db.IsRelational ? await _db.BeginTransactionAsync(ct) : null;

        await _db.SaveChangesAsync(ct);

        // Spread whatever is not booked against an invoice over the customer's oldest dues, and write
        // the result down — so the invoice list, its status filter, and the reminders all agree.
        await InvoiceAllocationSync.SyncAsync(_db, customer.Id, ct);

        if (tx is not null) await tx.CommitAsync(ct);

        // Audit trail: the payment row itself is the record; structured audit lands in Phase 15.
        _logger.LogInformation(
            "Payment {PaymentId} of {Amount} collected for customer {CustomerId} by {UserId}",
            payment.Id, payment.Amount, customer.Id, _currentUser.UserId);

        return payment.Id;
    }
}
