using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;

namespace ROCloud.Application.Features.Invoices.Queries.GetInvoicePdf;

/// <summary>Returns the invoice PDF bytes — streamed from storage if already generated, else built on the fly.</summary>
public sealed record GetInvoicePdfQuery(Guid Id) : IRequest<InvoicePdfResult>;

public sealed record InvoicePdfResult(byte[] Content, string FileName);

public class GetInvoicePdfQueryHandler : IRequestHandler<GetInvoicePdfQuery, InvoicePdfResult>
{
    private readonly IAppDbContext _db;
    private readonly IInvoicePdfGenerator _pdf;
    private readonly IFileStorage _storage;

    public GetInvoicePdfQueryHandler(IAppDbContext db, IInvoicePdfGenerator pdf, IFileStorage storage)
    {
        _db = db;
        _pdf = pdf;
        _storage = storage;
    }

    public async Task<InvoicePdfResult> Handle(GetInvoicePdfQuery request, CancellationToken ct)
    {
        var invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.Id == request.Id, ct)
                      ?? throw new NotFoundException("Invoice", request.Id);

        var fileName = $"{invoice.InvoiceNumber}.pdf";

        if (!string.IsNullOrWhiteSpace(invoice.PdfUrl) && await _storage.ExistsAsync(invoice.PdfUrl, ct))
        {
            await using var stored = await _storage.DownloadAsync(invoice.PdfUrl, ct);
            using var ms = new MemoryStream();
            await stored.CopyToAsync(ms, ct);
            return new InvoicePdfResult(ms.ToArray(), fileName);
        }

        var model = await InvoicePdfModelBuilder.BuildAsync(_db, invoice, ct);
        return new InvoicePdfResult(_pdf.Generate(model), fileName);
    }
}
