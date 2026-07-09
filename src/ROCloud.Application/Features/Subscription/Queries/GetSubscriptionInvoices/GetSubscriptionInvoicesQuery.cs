using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Subscription.Dtos;

namespace ROCloud.Application.Features.Subscription.Queries.GetSubscriptionInvoices;

/// <summary>The current tenant's ROCloud subscription invoices, newest first (guide §25).</summary>
public sealed record GetSubscriptionInvoicesQuery : IRequest<IReadOnlyList<SubscriptionInvoiceDto>>;

public class GetSubscriptionInvoicesQueryHandler
    : IRequestHandler<GetSubscriptionInvoicesQuery, IReadOnlyList<SubscriptionInvoiceDto>>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;

    public GetSubscriptionInvoicesQueryHandler(IAppDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<SubscriptionInvoiceDto>> Handle(
        GetSubscriptionInvoicesQuery request, CancellationToken ct)
    {
        // subscription_invoices is platform-owned (no tenant query filter) → scope explicitly.
        return await _db.SubscriptionInvoices
            .Where(i => i.TenantId == _tenant.TenantId)
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new SubscriptionInvoiceDto(
                i.Id, i.InvoiceNumber, i.PlanType, i.BillingCycle,
                i.PeriodStart, i.PeriodEnd, i.GrossAmount, i.DiscountAmount, i.Amount,
                i.Status, i.DueDate, i.Description, i.PaidAt, i.PdfUrl != null))
            .ToListAsync(ct);
    }
}
