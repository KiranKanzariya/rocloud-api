using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;

namespace ROCloud.Application.Features.Invoices.Queries.GetInvoicePdf;

/// <summary>Returns the invoice PDF bytes, rendered from the invoice row on every request — PDFs are
/// not stored, the database is the only copy.</summary>
public sealed record GetInvoicePdfQuery(Guid Id) : IRequest<InvoicePdfResult>;

public sealed record InvoicePdfResult(byte[] Content, string FileName);

public class GetInvoicePdfQueryHandler : IRequestHandler<GetInvoicePdfQuery, InvoicePdfResult>
{
    private readonly IAppDbContext _db;
    private readonly IInvoicePdfGenerator _pdf;

    public GetInvoicePdfQueryHandler(IAppDbContext db, IInvoicePdfGenerator pdf)
    {
        _db = db;
        _pdf = pdf;
    }

    public async Task<InvoicePdfResult> Handle(GetInvoicePdfQuery request, CancellationToken ct)
    {
        var invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.Id == request.Id, ct)
                      ?? throw new NotFoundException("Invoice", request.Id);

        var model = await InvoicePdfModelBuilder.BuildAsync(_db, invoice, ct);
        return new InvoicePdfResult(_pdf.Generate(model), $"{invoice.InvoiceNumber}.pdf");
    }
}
