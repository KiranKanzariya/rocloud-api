using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Subscription.Services;

namespace ROCloud.Application.Features.Subscription.Queries.GetSubscriptionInvoicePdf;

/// <summary>The subscription-invoice PDF bytes, rendered from the invoice row on every request — PDFs
/// are not stored, the database is the only copy.</summary>
public sealed record GetSubscriptionInvoicePdfQuery(Guid InvoiceId) : IRequest<SubscriptionInvoicePdfResult>;

public sealed record SubscriptionInvoicePdfResult(byte[] Bytes, string FileName);

public class GetSubscriptionInvoicePdfQueryHandler
    : IRequestHandler<GetSubscriptionInvoicePdfQuery, SubscriptionInvoicePdfResult>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ISubscriptionInvoicePdfGenerator _pdf;

    public GetSubscriptionInvoicePdfQueryHandler(
        IAppDbContext db, ITenantContext tenant, ISubscriptionInvoicePdfGenerator pdf)
    {
        _db = db;
        _tenant = tenant;
        _pdf = pdf;
    }

    public async Task<SubscriptionInvoicePdfResult> Handle(GetSubscriptionInvoicePdfQuery request, CancellationToken ct)
    {
        // subscription_invoices is platform-owned (no tenant query filter) → scope explicitly.
        var invoice = await _db.SubscriptionInvoices
            .FirstOrDefaultAsync(i => i.Id == request.InvoiceId && i.TenantId == _tenant.TenantId, ct)
            ?? throw new NotFoundException("SubscriptionInvoice", request.InvoiceId);

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == invoice.TenantId, ct)
                     ?? throw new NotFoundException("Tenant", invoice.TenantId);

        var paid = invoice.Status == SubscriptionInvoiceStatus.Paid;
        var bytes = _pdf.Generate(SubscriptionInvoicePdfModelBuilder.Build(invoice, tenant, paid));
        return new SubscriptionInvoicePdfResult(bytes, $"{invoice.InvoiceNumber}.pdf");
    }
}
