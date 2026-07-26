using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Subscription.Dtos;

namespace ROCloud.Application.Features.Platform.Billing.Queries.GetBillingInvoice;

/// <summary>
/// The subscription invoice a billing transaction paid for, as data (for the admin billing detail to
/// render inline next to the transaction). Platform counterpart of the owner's GetSubscriptionInvoiceById
/// — scoped by the TRANSACTION rather than tenant context, since the admin acts across tenants. Reuses
/// <see cref="SubscriptionInvoiceDto"/> so the admin shows exactly the invoice the owner sees.
/// </summary>
public sealed record GetBillingInvoiceQuery(Guid TransactionId) : IRequest<SubscriptionInvoiceDto>;

public class GetBillingInvoiceQueryHandler : IRequestHandler<GetBillingInvoiceQuery, SubscriptionInvoiceDto>
{
    private readonly IAppDbContext _db;

    public GetBillingInvoiceQueryHandler(IAppDbContext db) => _db = db;

    public async Task<SubscriptionInvoiceDto> Handle(GetBillingInvoiceQuery request, CancellationToken ct)
    {
        var txn = await _db.PlatformBillingTransactions
            .FirstOrDefaultAsync(t => t.Id == request.TransactionId, ct)
            ?? throw new NotFoundException("BillingTransaction", request.TransactionId);

        // Legacy row or a free (₹0) upgrade may have no linked invoice — 404 so the UI hides the panel.
        if (txn.SubscriptionInvoiceId is not { } invoiceId)
            throw new NotFoundException("SubscriptionInvoice", request.TransactionId);

        var i = await _db.SubscriptionInvoices.FirstOrDefaultAsync(x => x.Id == invoiceId, ct)
                ?? throw new NotFoundException("SubscriptionInvoice", invoiceId);

        return new SubscriptionInvoiceDto(
            i.Id, i.InvoiceNumber, i.PlanType, i.BillingCycle,
            i.PeriodStart, i.PeriodEnd, i.GrossAmount, i.DiscountAmount, i.Amount,
            i.Status, i.DueDate, i.Description, i.PaidAt);
    }
}
