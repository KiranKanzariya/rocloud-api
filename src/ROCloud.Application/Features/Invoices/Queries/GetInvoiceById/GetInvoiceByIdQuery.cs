using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Invoices.Dtos;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Invoices.Queries.GetInvoiceById;

public sealed record GetInvoiceByIdQuery(Guid Id) : IRequest<InvoiceDto>;

public class GetInvoiceByIdQueryHandler : IRequestHandler<GetInvoiceByIdQuery, InvoiceDto>
{
    private readonly IAppDbContext _db;

    public GetInvoiceByIdQueryHandler(IAppDbContext db) => _db = db;

    public async Task<InvoiceDto> Handle(GetInvoiceByIdQuery request, CancellationToken ct)
    {
        var invoice = await _db.Invoices
            .Include(i => i.Customer)
            .FirstOrDefaultAsync(i => i.Id == request.Id, ct)
            ?? throw new NotFoundException("Invoice", request.Id);

        var from = invoice.PeriodFrom ?? invoice.InvoiceDate;
        var to = invoice.PeriodTo ?? invoice.InvoiceDate;
        var lines = await InvoiceLineBuilder.BuildAsync(_db, invoice.CustomerId, from, to, ct);

        // PaidAmount is maintained on write and already includes money the owner recorded against the
        // customer rather than this invoice. Those payments carry no link to it, so they will not
        // appear in its payment list — work out how much came that way and say so on the page, rather
        // than showing a "Paid" invoice with no receipts against it.
        var linked = await _db.Payments
            .Where(p => p.InvoiceId == invoice.Id && p.Status == PaymentStatus.Completed)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;
        var allocated = Math.Max(0m, invoice.PaidAmount - linked);

        return new InvoiceDto(
            invoice.Id,
            invoice.InvoiceNumber,
            invoice.CustomerId,
            invoice.Customer?.Name ?? string.Empty,
            invoice.Customer?.Mobile,
            invoice.InvoiceDate,
            invoice.DueDate,
            invoice.PeriodFrom,
            invoice.PeriodTo,
            invoice.SubTotal,
            invoice.TaxAmount,
            invoice.Discount,
            invoice.TotalAmount,
            invoice.PaidAmount,
            invoice.TotalAmount - invoice.PaidAmount,
            allocated,
            invoice.Status.ToString(),
            invoice.GstNumber,
            invoice.Notes,
            invoice.CreatedAt,
            lines);
    }
}
