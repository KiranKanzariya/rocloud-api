using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Invoices.Dtos;

namespace ROCloud.Infrastructure.Pdf;

/// <summary>GST tax-invoice renderer (guide §10) using QuestPDF (Community licence — free for SMBs).</summary>
public class InvoicePdfGenerator : IInvoicePdfGenerator
{
    static InvoicePdfGenerator() => QuestPDF.Settings.License = LicenseType.Community;

    public byte[] Generate(InvoicePdfModel m)
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
                {
                    t.Span("This is a computer-generated invoice. ").FontSize(8).FontColor(Colors.Grey.Medium);
                    t.Span("HSN 2201 — packaged drinking water.").FontSize(8).FontColor(Colors.Grey.Medium);
                });
            });
        });

        return document.GeneratePdf();
    }

    private static void Header(IContainer container, InvoicePdfModel m)
    {
        container.Column(col =>
        {
            col.Item().Text("TAX INVOICE").FontSize(16).Bold().FontColor(Colors.Blue.Darken2);
            col.Item().PaddingTop(4).Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text(m.BusinessName).Bold();
                    if (m.BusinessAddress is not null) left.Item().Text(m.BusinessAddress).FontSize(9);
                    if (m.BusinessGstin is not null) left.Item().Text($"GSTIN: {m.BusinessGstin}").FontSize(9);
                });
                row.ConstantItem(190).Column(right =>
                {
                    right.Item().AlignRight().Text($"Invoice #: {m.InvoiceNumber}").Bold();
                    right.Item().AlignRight().Text($"Date: {m.InvoiceDate:dd MMM yyyy}").FontSize(9);
                    right.Item().AlignRight().Text($"Due: {m.DueDate:dd MMM yyyy}").FontSize(9);
                    if (m.PeriodFrom is { } pf && m.PeriodTo is { } pt)
                        right.Item().AlignRight().Text($"Period: {pf:dd MMM} – {pt:dd MMM yyyy}").FontSize(9);
                });
            });
            col.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
        });
    }

    private static void Content(IContainer container, InvoicePdfModel m)
    {
        container.PaddingVertical(10).Column(col =>
        {
            col.Item().Text("Bill To").FontSize(9).FontColor(Colors.Grey.Darken1);
            col.Item().Text(m.CustomerName).Bold();
            if (m.CustomerMobile is not null) col.Item().Text($"Mobile: {m.CustomerMobile}").FontSize(9);
            if (m.CustomerGstin is not null) col.Item().Text($"GSTIN: {m.CustomerGstin}").FontSize(9);

            col.Item().PaddingTop(10).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(4);   // description
                    c.RelativeColumn(1.3f); // HSN
                    c.RelativeColumn(1);   // qty
                    c.RelativeColumn(1.4f); // rate
                    c.RelativeColumn(1.6f); // amount
                });

                table.Header(header =>
                {
                    HeaderCell(header, "Description");
                    HeaderCell(header, "HSN");
                    HeaderCell(header, "Qty");
                    HeaderCell(header, "Rate");
                    HeaderCell(header, "Amount");
                });

                foreach (var line in m.Lines)
                {
                    BodyCell(table, line.Description);
                    BodyCell(table, line.Hsn);
                    BodyCell(table, line.Quantity.ToString());
                    BodyCell(table, Money(line.Rate));
                    BodyCell(table, Money(line.Amount));
                }
            });

            col.Item().PaddingTop(10).AlignRight().Column(totals =>
            {
                TotalRow(totals, "Subtotal", m.SubTotal);
                if (m.Discount > 0) TotalRow(totals, "Discount", -m.Discount);
                // GST may be turned off for this tenant (§24) — omit the tax rows when there's no tax.
                if (m.CgstAmount > 0) TotalRow(totals, "CGST", m.CgstAmount);
                if (m.SgstAmount > 0) TotalRow(totals, "SGST", m.SgstAmount);
                totals.Item().PaddingTop(2).Text($"Total: {Money(m.TotalAmount)}").Bold().FontSize(12);
            });

            if (!string.IsNullOrWhiteSpace(m.Notes))
                col.Item().PaddingTop(12).Text($"Notes: {m.Notes}").FontSize(9).FontColor(Colors.Grey.Darken1);
        });
    }

    private static void HeaderCell(TableCellDescriptor header, string text) =>
        header.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text(text).Bold().FontSize(9);

    private static void BodyCell(TableDescriptor table, string text) =>
        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(text).FontSize(9);

    private static void TotalRow(ColumnDescriptor col, string label, decimal amount) =>
        col.Item().Text($"{label}: {Money(amount)}").FontSize(10);

    private static string Money(decimal value) => $"₹{value:N2}";
}
