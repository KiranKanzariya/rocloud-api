using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Customers.Commands.SetCustomerOpeningBalance;
using ROCloud.Application.Features.Invoices.Dtos;
using ROCloud.Domain.Entities.Tenant;

namespace ROCloud.Application.Features.Invoices;

/// <summary>Builds the <see cref="InvoicePdfModel"/> for an invoice (seller, buyer, lines, GST split).</summary>
internal static class InvoicePdfModelBuilder
{
    private const string PackagedWaterHsn = "2201";   // HSN for packaged drinking water

    public static async Task<InvoicePdfModel> BuildAsync(IAppDbContext db, Invoice invoice, CancellationToken ct)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == invoice.CustomerId, ct)
                       ?? throw new NotFoundException("Customer", invoice.CustomerId);

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == invoice.TenantId, ct)
                     ?? throw new NotFoundException("Tenant", invoice.TenantId);

        var from = invoice.PeriodFrom ?? invoice.InvoiceDate;
        var to = invoice.PeriodTo ?? invoice.InvoiceDate;
        var lines = await InvoiceLineBuilder.BuildAsync(db, customer.Id, from, to, ct);

        var pdfLines = lines
            .Select(l => new InvoicePdfLine(
                $"{l.ProductName} ({l.BottleSize})", l.Hsn ?? PackagedWaterHsn, l.Quantity, l.Rate, l.Amount))
            .ToList();

        // Intra-state assumption: split the tax evenly into CGST + SGST (customer state not modelled).
        var cgst = Math.Round(invoice.TaxAmount / 2m, 2);
        var sgst = invoice.TaxAmount - cgst;

        var businessAddress = string.Join(", ",
            new[] { tenant.AddressLine, tenant.City, tenant.State, tenant.Pincode }
                .Where(s => !string.IsNullOrWhiteSpace(s)));

        return new InvoicePdfModel(
            invoice.InvoiceNumber,
            invoice.InvoiceDate,
            invoice.DueDate,
            invoice.PeriodFrom,
            invoice.PeriodTo,
            tenant.Name,
            tenant.GstNumber,
            string.IsNullOrWhiteSpace(businessAddress) ? null : businessAddress,
            customer.Name,
            customer.Mobile,
            invoice.GstNumber,
            pdfLines,
            invoice.SubTotal,
            cgst,
            sgst,
            invoice.Discount,
            invoice.TotalAmount,
            CleanNotes(invoice.Notes),
            // A tax invoice (title + HSN + GST) only when the tenant is GST-registered; otherwise a bill of supply.
            tenant.GstEnabled,
            invoice.Status.ToString(),
            invoice.PaidAmount,
            tenant.PrimaryColor);
    }

    /// <summary>Strips internal note markers (e.g. the opening-balance tag) so they never leak onto the
    /// customer-facing PDF — an opening-balance invoice then just reads "Carried forward from book".</summary>
    private static string? CleanNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return null;
        var cleaned = notes.Replace(SetCustomerOpeningBalanceCommand.Marker, "").Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }
}
