using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Customers;
using ROCloud.Application.Features.Payments.Dtos;

namespace ROCloud.Application.Features.Payments.Queries.GetCustomerUpiQr;

/// <summary>
/// The scan-to-pay payload for what a customer owes RIGHT NOW, so the owner can show a QR on screen
/// at the plant counter instead of waiting for an invoice PDF to be issued and read.
///
/// <para>The payload is built here rather than in the browser on purpose: the rules about what may be
/// asked for — the tenant has opted in, has a VPA, and the balance is above zero — already live in
/// <see cref="UpiPaymentLink"/> and on the invoice path. Rebuilding them in TypeScript would be a
/// second place for them to drift, and drift here means showing a QR for the wrong amount.</para>
/// </summary>
public sealed record GetCustomerUpiQrQuery(Guid CustomerId) : IRequest<CustomerUpiQrDto>;

public class GetCustomerUpiQrQueryHandler : IRequestHandler<GetCustomerUpiQrQuery, CustomerUpiQrDto>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;

    public GetCustomerUpiQrQueryHandler(IAppDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<CustomerUpiQrDto> Handle(GetCustomerUpiQrQuery request, CancellationToken ct)
    {
        var customer = await _db.Customers.AsNoTracking()
            .Where(c => c.Id == request.CustomerId)
            .Select(c => new { c.Id, c.Name, c.CustomerCode })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Customer", request.CustomerId);

        // Tenants is a platform table (not tenant-filtered) — scope explicitly, as GetTenantSettings does.
        var tenant = await _db.Tenants.AsNoTracking()
            .Where(t => t.Id == _tenant.TenantId)
            .Select(t => new { t.Name, t.UpiVpa, t.UpiPayeeName, t.UpiQrEnabled })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Tenant", _tenant.TenantId);

        // Same ledger the customer page and the money-in list show: billed − paid, across invoices AND
        // uninvoiced delivered orders. Anything else and the counter QR would disagree with the screen
        // the owner is looking at while the customer stands there.
        var balance = await CustomerBalance.ComputeAsync(_db, customer.Id, ct);

        // Identify the payer in the owner's UPI app. There is no single invoice to name here — the
        // balance can span several invoices and uninvoiced orders — so the customer code carries it,
        // with the name so the owner recognises it without a lookup.
        var note = UpiPaymentLink.Reference(customer.CustomerCode, customer.Name);

        // The same gate the invoice PDF applies, so the counter QR and the printed one can never
        // disagree about whether there is anything to show.
        var ready = tenant.UpiQrEnabled && !string.IsNullOrWhiteSpace(tenant.UpiVpa);

        var payload = ready
            ? UpiPaymentLink.Build(tenant.UpiVpa, tenant.UpiPayeeName ?? tenant.Name, balance, note)
            : null;

        return new CustomerUpiQrDto(
            customer.Id,
            customer.Name,
            balance,
            payload,
            payload is null ? null : tenant.UpiVpa,
            // Tells the UI WHY there is nothing to show, so it can point at settings rather than
            // render a blank box.
            Configured: ready);
    }
}
