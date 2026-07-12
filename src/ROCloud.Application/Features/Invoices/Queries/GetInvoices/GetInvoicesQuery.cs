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

        var rows = await query
            .OrderByDescending(i => i.InvoiceDate).ThenByDescending(i => i.InvoiceNumber)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(i => new
            {
                i.Id,
                i.InvoiceNumber,
                i.CustomerId,
                CustomerName = i.Customer != null ? i.Customer.Name : string.Empty,
                i.InvoiceDate,
                i.DueDate,
                i.TotalAmount,
                i.PaidAmount,
                i.Status,
                i.Discount,
                i.CreatedAt
            })
            .ToListAsync(ct);

        // Top up each row with its share of the customer's unallocated payment pool, so an invoice the
        // owner settled with a lump sum on the customer page doesn't read "Sent" for ever. NOTE: the
        // Status FILTER above runs on the RECORDED status in SQL, so a row can be filtered as Sent and
        // then render as Paid — the money is right, the filter is just coarse.
        var allocations = (await CustomerObligationAllocator.ComputeAsync(
            _db, rows.Select(r => r.CustomerId).Distinct().ToList(), ct)).Invoices;

        var items = rows.Select(r =>
        {
            var (paid, balance, status) = InvoicePaymentStatus.Resolve(
                r.Status, r.TotalAmount, r.PaidAmount, allocations.GetValueOrDefault(r.Id, 0m));
            return new InvoiceListItemDto(
                r.Id, r.InvoiceNumber, r.CustomerId, r.CustomerName, r.InvoiceDate, r.DueDate,
                r.TotalAmount, paid, balance, status.ToString(), r.Discount, r.CreatedAt);
        }).ToList();

        return new PagedResult<InvoiceListItemDto>(items, total, page, pageSize);
    }
}
