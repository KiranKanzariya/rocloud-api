using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Models;
using ROCloud.Application.Features.Payments.Dtos;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Payments.Queries.GetPayments;

public sealed record GetPaymentsQuery(PaymentFilterDto Filter) : IRequest<PagedResult<PaymentListItemDto>>;

public class GetPaymentsQueryHandler : IRequestHandler<GetPaymentsQuery, PagedResult<PaymentListItemDto>>
{
    private readonly IAppDbContext _db;

    public GetPaymentsQueryHandler(IAppDbContext db) => _db = db;

    public async Task<PagedResult<PaymentListItemDto>> Handle(GetPaymentsQuery request, CancellationToken ct)
    {
        var f = request.Filter;
        var page = Math.Max(1, f.Page);
        var pageSize = Math.Clamp(f.PageSize, 1, 100);

        IQueryable<Payment> query = _db.Payments;

        if (f.CustomerId is { } customerId) query = query.Where(p => p.CustomerId == customerId);
        if (f.InvoiceId is { } invoiceId) query = query.Where(p => p.InvoiceId == invoiceId);
        if (f.PaymentMethod is not null && Enum.GetNames<PaymentMethod>().Contains(f.PaymentMethod))
        {
            var method = Enum.Parse<PaymentMethod>(f.PaymentMethod);
            query = query.Where(p => p.PaymentMethod == method);
        }
        if (f.Status is not null && Enum.GetNames<PaymentStatus>().Contains(f.Status))
        {
            var status = Enum.Parse<PaymentStatus>(f.Status);
            query = query.Where(p => p.Status == status);
        }
        if (f.FromDate is { } from)
        {
            var fromTs = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(p => p.PaidAt >= fromTs);
        }
        if (f.ToDate is { } to)
        {
            var toTs = to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            query = query.Where(p => p.PaidAt <= toTs);
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(p => p.PaidAt).ThenByDescending(p => p.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new PaymentListItemDto(
                p.Id,
                p.CustomerId,
                p.Customer != null ? p.Customer.Name : string.Empty,
                p.InvoiceId,
                p.Invoice != null ? p.Invoice.InvoiceNumber : null,
                p.OrderId,
                p.Amount,
                p.PaymentMethod.ToString(),
                p.Status.ToString(),
                p.ReferenceNumber,
                p.CollectedBy,
                p.PaidAt))
            .ToListAsync(ct);

        return new PagedResult<PaymentListItemDto>(items, total, page, pageSize);
    }
}
