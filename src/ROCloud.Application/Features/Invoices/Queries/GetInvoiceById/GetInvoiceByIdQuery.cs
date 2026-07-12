using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Invoices.Dtos;
using ROCloud.Application.Features.Payments;

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

        // Payments the owner recorded against the customer rather than this invoice still settle it —
        // FIFO, oldest obligation first. Report the resolved money, and how much of it came that way.
        var allocations = (await CustomerObligationAllocator.ComputeAsync(
            _db, new[] { invoice.CustomerId }, ct)).Invoices;
        var allocated = allocations.GetValueOrDefault(invoice.Id, 0m);
        var (paidAmount, balance, status) = InvoicePaymentStatus.Resolve(
            invoice.Status, invoice.TotalAmount, invoice.PaidAmount, allocated);

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
            paidAmount,
            balance,
            allocated,
            status.ToString(),
            invoice.GstNumber,
            invoice.Notes,
            invoice.PdfUrl,
            invoice.CreatedAt,
            lines);
    }
}
