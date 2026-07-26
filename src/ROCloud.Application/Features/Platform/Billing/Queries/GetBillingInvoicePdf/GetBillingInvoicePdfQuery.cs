using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Subscription.Services;

namespace ROCloud.Application.Features.Platform.Billing.Queries.GetBillingInvoicePdf;

/// <summary>
/// The subscription-invoice PDF for a billing transaction, rendered on demand (PDFs aren't stored). The
/// platform counterpart of the owner's GetSubscriptionInvoicePdf: scoped by the TRANSACTION rather than
/// the tenant context, since the admin acts across tenants. Both reuse the same generator, so the admin
/// sees exactly the document the owner has.
/// </summary>
public sealed record GetBillingInvoicePdfQuery(Guid TransactionId) : IRequest<BillingInvoicePdfResult>;

public sealed record BillingInvoicePdfResult(byte[] Bytes, string FileName);

public class GetBillingInvoicePdfQueryHandler
    : IRequestHandler<GetBillingInvoicePdfQuery, BillingInvoicePdfResult>
{
    private readonly IAppDbContext _db;
    private readonly ISubscriptionInvoicePdfGenerator _pdf;

    public GetBillingInvoicePdfQueryHandler(IAppDbContext db, ISubscriptionInvoicePdfGenerator pdf)
    {
        _db = db;
        _pdf = pdf;
    }

    public async Task<BillingInvoicePdfResult> Handle(GetBillingInvoicePdfQuery request, CancellationToken ct)
    {
        var txn = await _db.PlatformBillingTransactions
            .FirstOrDefaultAsync(t => t.Id == request.TransactionId, ct)
            ?? throw new NotFoundException("BillingTransaction", request.TransactionId);

        // A legacy row or a free (₹0) upgrade may have no linked invoice — nothing to render.
        if (txn.SubscriptionInvoiceId is not { } invoiceId)
            throw new NotFoundException("SubscriptionInvoice", request.TransactionId);

        var invoice = await _db.SubscriptionInvoices.FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
                      ?? throw new NotFoundException("SubscriptionInvoice", invoiceId);

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == invoice.TenantId, ct)
                     ?? throw new NotFoundException("Tenant", invoice.TenantId);

        var paid = invoice.Status == SubscriptionInvoiceStatus.Paid;
        var bytes = _pdf.Generate(SubscriptionInvoicePdfModelBuilder.Build(invoice, tenant, paid));
        return new BillingInvoicePdfResult(bytes, $"{invoice.InvoiceNumber}.pdf");
    }
}
