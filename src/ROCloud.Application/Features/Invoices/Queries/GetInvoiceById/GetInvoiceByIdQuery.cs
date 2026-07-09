using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Invoices.Dtos;

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
            invoice.Status.ToString(),
            invoice.GstNumber,
            invoice.Notes,
            invoice.PdfUrl,
            invoice.CreatedAt,
            lines);
    }
}
