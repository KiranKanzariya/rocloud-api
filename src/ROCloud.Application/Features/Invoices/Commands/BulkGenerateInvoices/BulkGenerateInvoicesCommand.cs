using ROCloud.Application.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Settings;
using ROCloud.Application.Services;
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

/// <param name="Skipped">Total skipped = <paramref name="SkippedAlreadyInvoiced"/> + <paramref name="SkippedNothingDelivered"/>.</param>
/// <param name="SkippedAlreadyInvoiced">
/// Held an overlapping invoice, so the month was NOT billed and the owner must raise the remaining days
/// by hand. Broken out from the total because this one needs someone to act; a quiet month does not.
/// </param>
public sealed record BulkInvoiceResultDto(
    int InvoicesCreated,
    int CustomersConsidered,
    int Skipped,
    int SkippedAlreadyInvoiced = 0,
    int SkippedNothingDelivered = 0);

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
        var invoiceDate = AppTimeZone.Today(DateTime.UtcNow);
        var dueDate = invoiceDate.AddDays(request.DueInDays ?? _settings.InvoiceDueInDays);
        var prefix = InvoiceNumberGenerator.Prefix(invoiceDate);

        // One round-trip for the month's current sequence high-water mark (MAX suffix, not a row count —
        // a count re-mints an existing number when there is any gap; see InvoiceNumberGenerator).
        var seq = await InvoiceNumberGenerator.MaxSeqAsync(_db, _tenant.TenantId, invoiceDate, ct);

        // Idempotency guard: customers who already hold a non-cancelled invoice OVERLAPPING this period
        // are skipped. Exact-period matching would only catch a re-run (admin "run now", owner re-trigger,
        // retry); it would miss a manual invoice covering part of the month — say 05–10 Jul raised for a
        // customer's function — and bill those days a second time. A customer's balance sums invoices
        // GROSS (CustomerBalance), so that is real money added to what they are chased for.
        // The trade-off: the rest of that month is then NOT auto-billed, and the owner must raise it by
        // hand. Under-billing is recoverable, double-billing costs trust — hence the skip, and hence the
        // per-customer log below so it is never silent.
        var alreadyInvoiced = (await _db.Invoices
                .Where(i => i.Status != InvoiceStatus.Cancelled
                            && i.PeriodFrom <= request.PeriodTo && i.PeriodTo >= request.PeriodFrom)
                .Select(i => i.CustomerId)
                .ToListAsync(ct))
            .ToHashSet();

        var created = 0;
        var skippedInvoiced = 0;
        var skippedEmpty = 0;
        var billed = new List<Guid>();

        foreach (var c in customers)
        {
            if (alreadyInvoiced.Contains(c.Id)) { skippedInvoiced++; continue; } // overlapping invoice

            var lines = await InvoiceLineBuilder.BuildAsync(_db, c.Id, request.PeriodFrom, request.PeriodTo, ct);
            if (lines.Count == 0) { skippedEmpty++; continue; }

            var subTotal = lines.Sum(l => l.Amount);
            // Each customer's standing (platform-set) discount applies automatically.
            var discount = CustomerDiscountCalculator.Compute(c.DiscountType, c.DiscountValue, subTotal);
            var taxable = Math.Max(0m, subTotal - discount);
            var taxAmount = Math.Round(taxable * gstRate, 2);

            // Snapshot what is owed on everything else, BEFORE this invoice exists — see InvoicePreviousDue.
            // Nothing generated earlier in this loop belongs to this customer (one invoice each), so the
            // still-unsaved invoices above cannot affect it.
            var previousDue = await InvoicePreviousDue.ComputeAsync(_db, c.Id, subTotal, ct);

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
                PreviousDue = previousDue,
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
            // The batch and its settlement are one fact — commit both or neither, so a sync failure
            // can't leave a whole run of invoices un-settled against advances customers already hold.
            // Guarded for the non-relational in-memory test provider.
            await using var tx = _db.IsRelational ? await _db.BeginTransactionAsync(ct) : null;

            await _db.SaveChangesAsync(ct);

            // A freshly raised invoice may already be covered by an advance the customer holds, so
            // re-settle each one we billed rather than sending them a demand they have already paid.
            // Re-settle all of them, then persist in a single transaction (not one save per customer).
            foreach (var customerId in billed)
                await Payments.InvoiceAllocationSync.SyncWithoutSaveAsync(_db, customerId, ct);
            await _db.SaveChangesAsync(ct);

            if (tx is not null) await tx.CommitAsync(ct);
        }

        return new BulkInvoiceResultDto(
            created, customers.Count, skippedInvoiced + skippedEmpty, skippedInvoiced, skippedEmpty);
    }
}
