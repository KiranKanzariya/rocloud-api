using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Models;
using ROCloud.Application.Features.Invoices.Dtos;
using ROCloud.Application.Features.Payments;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Invoices.Queries.GetInvoices;

public sealed record GetInvoicesQuery(InvoiceFilterDto Filter) : IRequest<PagedResult<InvoiceListItemDto>>;

public class GetInvoicesQueryHandler : IRequestHandler<GetInvoicesQuery, PagedResult<InvoiceListItemDto>>
{
    private readonly IAppDbContext _db;

    public GetInvoicesQueryHandler(IAppDbContext db) => _db = db;

    public async Task<PagedResult<InvoiceListItemDto>> Handle(GetInvoicesQuery request, CancellationToken ct)
    {
        var f = request.Filter;
        var page = Math.Max(1, f.Page);
        var pageSize = Math.Clamp(f.PageSize, 1, 100);

        IQueryable<Invoice> query = _db.Invoices;

        if (f.CustomerId is { } customerId) query = query.Where(i => i.CustomerId == customerId);
        if (f.PeriodFrom is { } from) query = query.Where(i => i.InvoiceDate >= from);
        if (f.PeriodTo is { } to) query = query.Where(i => i.InvoiceDate <= to);
        if (f.Status is not null && Enum.GetNames<InvoiceStatus>().Contains(f.Status))
        {
            var status = Enum.Parse<InvoiceStatus>(f.Status);
            query = query.Where(i => i.Status == status);
        }

        var total = await query.CountAsync(ct);

        // PaidAmount and Status are maintained on write by InvoiceAllocationSync — they already account
        // for payments the owner recorded against the customer rather than the invoice. So the rows can
        // be read straight from the database, and the Status filter above (which runs in SQL) agrees
        // with what is rendered. While the status was DERIVED here, it could not: filtering by Sent
        // returned invoices that displayed as Paid, and filtering by Paid missed settled ones.
        var items = await query
            .OrderByDescending(i => i.InvoiceDate).ThenByDescending(i => i.InvoiceNumber)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(i => new InvoiceListItemDto(
                i.Id,
                i.InvoiceNumber,
                i.CustomerId,
                i.Customer != null ? i.Customer.Name : string.Empty,
                i.InvoiceDate,
                i.DueDate,
                i.TotalAmount,
                i.PaidAmount,
                i.TotalAmount - i.PaidAmount,
                i.Status.ToString(),
                i.Discount,
                i.CreatedAt))
            .ToListAsync(ct);

        return new PagedResult<InvoiceListItemDto>(items, total, page, pageSize);
    }
}
