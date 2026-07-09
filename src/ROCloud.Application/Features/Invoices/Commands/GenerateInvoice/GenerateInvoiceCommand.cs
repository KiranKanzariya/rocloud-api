using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Sanitisation;
using ROCloud.Application.Common.Settings;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Features.Invoices.Commands.GenerateInvoice;

/// <summary>
/// Generates an invoice for a customer over [PeriodFrom, PeriodTo] from their Delivered
/// orders. GstRate defaults to 0.18 (18% on packaged drinking water) when null.
/// </summary>
public sealed record GenerateInvoiceCommand(
    Guid CustomerId,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    decimal? GstRate,
    decimal? Discount,
    int? DueInDays,
    [property: SanitizeHtml] string? Notes) : IRequest<Guid>;

public class GenerateInvoiceCommandValidator : AbstractValidator<GenerateInvoiceCommand>
{
    public GenerateInvoiceCommandValidator()
    {
        RuleFor(c => c.CustomerId).NotEmpty();
        RuleFor(c => c.PeriodTo).GreaterThanOrEqualTo(c => c.PeriodFrom)
            .WithMessage("PeriodTo must be on or after PeriodFrom.");
        RuleFor(c => c.GstRate).InclusiveBetween(0m, 1m).When(c => c.GstRate.HasValue)
            .WithMessage("GstRate must be a fraction between 0 and 1 (e.g. 0.18).");
        RuleFor(c => c.Discount).GreaterThanOrEqualTo(0m).When(c => c.Discount.HasValue);
    }
}

public class GenerateInvoiceCommandHandler : IRequestHandler<GenerateInvoiceCommand, Guid>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAppSettings _settings;

    public GenerateInvoiceCommandHandler(IAppDbContext db, ITenantContext tenant, IAppSettings settings)
    {
        _db = db;
        _tenant = tenant;
        _settings = settings;
    }

    public async Task<Guid> Handle(GenerateInvoiceCommand request, CancellationToken ct)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct)
                       ?? throw new NotFoundException("Customer", request.CustomerId);

        var lines = await InvoiceLineBuilder.BuildAsync(
            _db, customer.Id, request.PeriodFrom, request.PeriodTo, ct);

        if (lines.Count == 0)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["period"] = ["No delivered orders found for this customer in the selected period."]
            });

        var subTotal = lines.Sum(l => l.Amount);
        // GST is owner-configurable per tenant (§24): an explicit per-call rate wins; otherwise use the
        // tenant's rate when GST is enabled, or 0 when the owner has turned it off.
        var gst = await _db.Tenants.AsNoTracking()
            .Where(t => t.Id == _tenant.TenantId)
            .Select(t => new { t.GstEnabled, t.GstRate })
            .FirstOrDefaultAsync(ct);
        var gstRate = request.GstRate ?? (gst is { GstEnabled: true } ? gst.GstRate : 0m);
        // An explicit per-invoice discount overrides the customer's standing (platform-set) discount.
        var standingDiscount = CustomerDiscountCalculator.Compute(customer.DiscountType, customer.DiscountValue, subTotal);
        var discount = request.Discount ?? standingDiscount;
        var taxable = Math.Max(0m, subTotal - discount);
        var taxAmount = Math.Round(taxable * gstRate, 2);
        var totalAmount = taxable + taxAmount;

        var invoiceDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var dueDate = invoiceDate.AddDays(request.DueInDays ?? _settings.InvoiceDueInDays);

        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            CustomerId = customer.Id,
            InvoiceNumber = await NextInvoiceNumberAsync(invoiceDate, ct),
            InvoiceDate = invoiceDate,
            DueDate = dueDate,
            PeriodFrom = request.PeriodFrom,
            PeriodTo = request.PeriodTo,
            SubTotal = subTotal,
            TaxAmount = taxAmount,
            Discount = subTotal - taxable,   // effective discount (capped at subtotal)
            TotalAmount = totalAmount,
            PaidAmount = 0m,
            Status = InvoiceStatus.Draft,
            GstNumber = null,   // customer GSTIN is not modelled in v1
            Notes = request.Notes
        };

        _db.Invoices.Add(invoice);

        // Don't re-bill orders the customer already paid for (e.g. at the door): credit those payments
        // to the invoice so its paid amount / status are correct.
        await InvoicePaymentReconciler.CreditPriorPaymentsAsync(
            _db, invoice, customer.Id, request.PeriodFrom, request.PeriodTo, ct);

        await _db.SaveChangesAsync(ct);
        return invoice.Id;
    }

    /// <summary>INV-YYYYMM-NNNN, sequential per tenant per month (incl. soft-deleted so numbers aren't reused).</summary>
    private async Task<string> NextInvoiceNumberAsync(DateOnly invoiceDate, CancellationToken ct)
    {
        var prefix = $"INV-{invoiceDate:yyyyMM}-";
        var countThisMonth = await _db.Invoices.IgnoreQueryFilters()
            .CountAsync(i => i.TenantId == _tenant.TenantId && i.InvoiceNumber.StartsWith(prefix), ct);
        return $"{prefix}{countThisMonth + 1:D4}";
    }
}
