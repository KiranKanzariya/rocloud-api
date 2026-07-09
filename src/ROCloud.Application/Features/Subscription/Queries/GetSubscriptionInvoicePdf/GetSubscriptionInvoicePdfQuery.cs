using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;

namespace ROCloud.Application.Features.Subscription.Queries.GetSubscriptionInvoicePdf;

/// <summary>The stored PDF bytes of a subscription invoice, streamed to the client (guide §25).</summary>
public sealed record SubscriptionInvoicePdfResult(byte[] Bytes, string FileName);

public sealed record GetSubscriptionInvoicePdfQuery(Guid InvoiceId) : IRequest<SubscriptionInvoicePdfResult>;

public class GetSubscriptionInvoicePdfQueryHandler : IRequestHandler<GetSubscriptionInvoicePdfQuery, SubscriptionInvoicePdfResult>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IFileStorage _storage;

    public GetSubscriptionInvoicePdfQueryHandler(IAppDbContext db, ITenantContext tenant, IFileStorage storage)
    {
        _db = db;
        _tenant = tenant;
        _storage = storage;
    }

    public async Task<SubscriptionInvoicePdfResult> Handle(GetSubscriptionInvoicePdfQuery request, CancellationToken ct)
    {
        var invoice = await _db.SubscriptionInvoices
            .FirstOrDefaultAsync(i => i.Id == request.InvoiceId && i.TenantId == _tenant.TenantId, ct)
            ?? throw new NotFoundException("SubscriptionInvoice", request.InvoiceId);

        if (string.IsNullOrWhiteSpace(invoice.PdfUrl))
            throw new NotFoundException("SubscriptionInvoicePdf", request.InvoiceId);

        await using var stream = await _storage.DownloadAsync(invoice.PdfUrl, ct);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        return new SubscriptionInvoicePdfResult(ms.ToArray(), $"{invoice.InvoiceNumber}.pdf");
    }
}
