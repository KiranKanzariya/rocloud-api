using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Subscription.Dtos;

namespace ROCloud.Application.Features.Subscription.Queries.GetSubscriptionInvoiceById;

/// <summary>A single ROCloud subscription invoice of the current tenant (for the detail page).</summary>
public sealed record GetSubscriptionInvoiceByIdQuery(Guid Id) : IRequest<SubscriptionInvoiceDto>;

public class GetSubscriptionInvoiceByIdQueryHandler : IRequestHandler<GetSubscriptionInvoiceByIdQuery, SubscriptionInvoiceDto>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;

    public GetSubscriptionInvoiceByIdQueryHandler(IAppDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<SubscriptionInvoiceDto> Handle(GetSubscriptionInvoiceByIdQuery request, CancellationToken ct)
    {
        var i = await _db.SubscriptionInvoices
            .FirstOrDefaultAsync(x => x.Id == request.Id && x.TenantId == _tenant.TenantId, ct)
            ?? throw new NotFoundException("SubscriptionInvoice", request.Id);

        return new SubscriptionInvoiceDto(
            i.Id, i.InvoiceNumber, i.PlanType, i.BillingCycle,
            i.PeriodStart, i.PeriodEnd, i.GrossAmount, i.DiscountAmount, i.Amount,
            i.Status, i.DueDate, i.Description, i.PaidAt, i.PdfUrl != null);
    }
}
