using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Settings;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Invoices.Commands.BulkGenerateInvoices;

/// <summary>
/// Generates one invoice per Monthly-billed customer for [PeriodFrom, PeriodTo].
/// Customers with no delivered orders in the period are skipped, as are customers who already have a
/// non-cancelled invoice for the exact same period — so a re-run (manual trigger or retry) never
/// creates a duplicate invoice. (Hangfire trigger: Phase 14.)
/// </summary>
public sealed record BulkGenerateInvoicesCommand(
    DateOnly PeriodFrom, DateOnly PeriodTo, decimal? GstRate, int? DueInDays)
    : IRequest<BulkInvoiceResultDto>;

public sealed record BulkInvoiceResultDto(int InvoicesCreated, int CustomersConsidered, int Skipped);

public class BulkGenerateInvoicesCommandHandler
    : IRequestHandler<BulkGenerateInvoicesCommand, BulkInvoiceResultDto>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAppSettings _settings;

    public BulkGenerateInvoicesCommandHandler(IAppDbContext db, ITenantContext tenant, IAppSettings settings)
    {
        _db = db;
        _tenant = tenant;
        _settings = settings;
    }

    public async Task<BulkInvoiceResultDto> Handle(BulkGenerateInvoicesCommand request, CancellationToken ct)
    {
        var customers = await _db.Customers
            .Where(c => c.IsActive && c.PaymentPreference == PaymentPreference.Monthly)
            .Select(c => new { c.Id, c.DiscountType, c.DiscountValue })
            .ToListAsync(ct);

        // GST is owner-configurable per tenant (§24): explicit per-call rate wins, else the tenant's
        // rate when enabled, or 0 when off. Read once for the whole batch.
        var gst = await _db.Tenants.AsNoTracking()
            .Where(t => t.Id == _tenant.TenantId)
            .Select(t => new { t.GstEnabled, t.GstRate })
            .FirstOrDefaultAsync(ct);
        var gstRate = request.GstRate ?? (gst is { GstEnabled: true } ? gst.GstRate : 0m);
        var invoiceDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var dueDate = invoiceDate.AddDays(request.DueInDays ?? _settings.InvoiceDueInDays);
        var prefix = $"INV-{invoiceDate:yyyyMM}-";

        // One round-trip for the month's current sequence high-water mark.
        var seq = await _db.Invoices.IgnoreQueryFilters()
            .CountAsync(i => i.TenantId == _tenant.TenantId && i.InvoiceNumber.StartsWith(prefix), ct);

        // Idempotency guard: customers who already have a non-cancelled invoice for this exact period
        // are skipped, so a re-run (admin "run now", owner re-trigger, retry) never double-bills them.
        var alreadyInvoiced = (await _db.Invoices
                .Where(i => i.PeriodFrom == request.PeriodFrom && i.PeriodTo == request.PeriodTo
                            && i.Status != InvoiceStatus.Cancelled)
                .Select(i => i.CustomerId)
                .ToListAsync(ct))
            .ToHashSet();

        var created = 0;
        var skipped = 0;
        var billed = new List<Guid>();

        foreach (var c in customers)
        {
            if (alreadyInvoiced.Contains(c.Id)) { skipped++; continue; } // already billed for this period

            var lines = await InvoiceLineBuilder.BuildAsync(_db, c.Id, request.PeriodFrom, request.PeriodTo, ct);
            if (lines.Count == 0) { skipped++; continue; }

            var subTotal = lines.Sum(l => l.Amount);
            // Each customer's standing (platform-set) discount applies automatically.
            var discount = CustomerDiscountCalculator.Compute(c.DiscountType, c.DiscountValue, subTotal);
            var taxable = Math.Max(0m, subTotal - discount);
            var taxAmount = Math.Round(taxable * gstRate, 2);

            seq++;
            var invoice = new Invoice
            {
                Id = Guid.NewGuid(),
                TenantId = _tenant.TenantId,
                CustomerId = c.Id,
                InvoiceNumber = $"{prefix}{seq:D4}",
                InvoiceDate = invoiceDate,
                DueDate = dueDate,
                PeriodFrom = request.PeriodFrom,
                PeriodTo = request.PeriodTo,
                SubTotal = subTotal,
                TaxAmount = taxAmount,
                Discount = subTotal - taxable,
                TotalAmount = taxable + taxAmount,
                PaidAmount = 0m,
                Status = InvoiceStatus.Draft
            };
            _db.Invoices.Add(invoice);

            // Credit anything already paid against this period's orders so we don't re-bill it.
            await InvoicePaymentReconciler.CreditPriorPaymentsAsync(
                _db, invoice, c.Id, request.PeriodFrom, request.PeriodTo, ct);
            billed.Add(c.Id);
            created++;
        }

        if (created > 0)
        {
            await _db.SaveChangesAsync(ct);

            // A freshly raised invoice may already be covered by an advance the customer holds, so
            // re-settle each one we billed rather than sending them a demand they have already paid.
            foreach (var customerId in billed)
                await Payments.InvoiceAllocationSync.SyncAsync(_db, customerId, ct);
        }

        return new BulkInvoiceResultDto(created, customers.Count, skipped);
    }
}
