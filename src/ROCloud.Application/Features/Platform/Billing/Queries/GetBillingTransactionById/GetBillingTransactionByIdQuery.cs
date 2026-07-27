using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Platform.Billing.Dtos;

namespace ROCloud.Application.Features.Platform.Billing.Queries.GetBillingTransactionById;

/// <summary>
/// One platform billing transaction, for the admin billing detail page. The list hands its rows to
/// the table, but a deep-linked page only has the id from the route — hence this.
/// </summary>
public sealed record GetBillingTransactionByIdQuery(Guid Id) : IRequest<BillingTransactionDto>;

public class GetBillingTransactionByIdQueryHandler
    : IRequestHandler<GetBillingTransactionByIdQuery, BillingTransactionDto>
{
    private readonly IAppDbContext _db;

    public GetBillingTransactionByIdQueryHandler(IAppDbContext db) => _db = db;

    public async Task<BillingTransactionDto> Handle(GetBillingTransactionByIdQuery request, CancellationToken ct)
        // Same projection as GetBillingTransactionsQuery — the page and the list must not drift apart.
        => await _db.PlatformBillingTransactions
               .Where(t => t.Id == request.Id)
               .Select(t => new BillingTransactionDto(
                   t.Id, t.TenantId, t.Tenant!.Name, t.PlanType, t.Amount, t.BillingCycle,
                   t.Status, t.RazorpayPaymentId, t.CreatedAt,
                   t.SubscriptionInvoiceId,
                   t.SubscriptionInvoice != null ? t.SubscriptionInvoice.InvoiceNumber : null,
                   t.PaymentMethod, t.PaymentInstrument))
               .FirstOrDefaultAsync(ct)
           ?? throw new NotFoundException("BillingTransaction", request.Id);
}
