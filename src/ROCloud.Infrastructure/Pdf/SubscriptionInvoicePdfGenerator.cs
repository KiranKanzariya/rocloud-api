using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Subscription.Dtos;

namespace ROCloud.Infrastructure.Pdf;

/// <summary>
/// ROCloud subscription-invoice renderer (guide §25/§26) using QuestPDF. Seller = ROCloud, buyer =
/// the tenant. Simple net-amount layout for v1 (no GST split — decision §11.5). Reuses the same
/// visual style as the customer <see cref="InvoicePdfGenerator"/>.
/// </summary>
public class SubscriptionInvoicePdfGenerator : ISubscriptionInvoicePdfGenerator
{
    static SubscriptionInvoicePdfGenerator() => QuestPDF.Settings.License = LicenseType.Community;

    public byte[] Generate(SubscriptionInvoicePdfModel m)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(36);
                page.Size(PageSizes.A4);
                page.DefaultTextStyle(t => t.FontSize(10));

                page.Header().Element(h => Header(h, m));
                page.Content().Element(c => Content(c, m));
                page.Footer().AlignCenter().Text(t =>
                    t.Span("This is a computer-generated invoice for your ROCloud subscription.")
                        .FontSize(8).FontColor(Colors.Grey.Medium));
            });
        });

        return document.GeneratePdf();
    }

    private static void Header(IContainer container, SubscriptionInvoicePdfModel m)
    {
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Text(m.Paid ? "PAID INVOICE" : "INVOICE")
                    .FontSize(16).Bold().FontColor(m.Paid ? Colors.Green.Darken2 : Colors.Blue.Darken2);
                row.ConstantItem(60).AlignRight().Text("ROCloud").Bold().FontColor(Colors.Blue.Darken2);
            });
            col.Item().PaddingTop(4).Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text("ROCloud").Bold();
                    left.Item().Text("RO business management platform").FontSize(9).FontColor(Colors.Grey.Darken1);
                });
                row.ConstantItem(200).Column(right =>
                {
                    right.Item().AlignRight().Text($"Invoice #: {m.InvoiceNumber}").Bold();
                    right.Item().AlignRight().Text($"Date: {m.InvoiceDate:dd MMM yyyy}").FontSize(9);
                    right.Item().AlignRight().Text($"Due: {m.PeriodStart:dd MMM yyyy}").FontSize(9);
                    right.Item().AlignRight().Text($"Period: {m.PeriodStart:dd MMM} – {m.PeriodEnd:dd MMM yyyy}").FontSize(9);
                });
            });
            col.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
        });
    }

    private static void Content(IContainer container, SubscriptionInvoicePdfModel m)
    {
        container.PaddingVertical(10).Column(col =>
        {
            col.Item().Text("Bill To").FontSize(9).FontColor(Colors.Grey.Darken1);
            col.Item().Text(m.TenantName).Bold();
            if (!string.IsNullOrWhiteSpace(m.TenantGstin))
                col.Item().Text($"GSTIN: {m.TenantGstin}").FontSize(9);

            col.Item().PaddingTop(10).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(5);   // description
                    c.RelativeColumn(1.5f); // cycle
                    c.RelativeColumn(1.8f); // amount
                });

                table.Header(header =>
                {
                    HeaderCell(header, "Description");
                    HeaderCell(header, "Cycle");
                    HeaderCell(header, "Amount");
                });

                BodyCell(table, m.LineDescription);
                BodyCell(table, m.BillingCycle);
                BodyCell(table, Money(m.GrossAmount));
            });

            col.Item().PaddingTop(10).AlignRight().Column(totals =>
            {
                totals.Item().Text($"Subtotal: {Money(m.GrossAmount)}").FontSize(10);
                if (m.DiscountAmount > 0)
                    totals.Item().Text($"Discount: {Money(-m.DiscountAmount)}").FontSize(10);
                totals.Item().PaddingTop(2).Text($"Total: {Money(m.Amount)}").Bold().FontSize(12);
                if (m.Paid)
                    totals.Item().PaddingTop(2).Text("Status: PAID").Bold().FontColor(Colors.Green.Darken2);
            });
        });
    }

    private static void HeaderCell(TableCellDescriptor header, string text) =>
        header.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text(text).Bold().FontSize(9);

    private static void BodyCell(TableDescriptor table, string text) =>
        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(text).FontSize(9);

    private static string Money(decimal value) => $"₹{value:N2}";
}
