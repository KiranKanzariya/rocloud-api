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
                page.Footer().AlignCenter()
                    .Text($"This is a computer-generated invoice from {m.BusinessName}.")
                    .FontSize(8).FontColor(Colors.Grey.Medium);
            });
        });

        return document.GeneratePdf();
    }

    private static void Header(IContainer container, InvoicePdfModel m)
    {
        container.Column(col =>
        {
            col.Item().Text("INVOICE").FontSize(16).Bold().FontColor(Brand(m));
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

                    var (statusText, statusColor) = StatusLabel(m.Status, m.TotalAmount - m.PaidAmount);
                    right.Item().AlignRight().PaddingTop(3).Text(statusText).Bold().FontColor(statusColor);
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
                // HSN is a GST tax-invoice requirement — omit the column on a bill of supply.
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(4);   // description
                    if (m.IsTaxInvoice) c.RelativeColumn(1.3f); // HSN
                    c.RelativeColumn(1);   // qty
                    c.RelativeColumn(1.4f); // rate
                    c.RelativeColumn(1.6f); // amount
                });

                table.Header(header =>
                {
                    HeaderCell(header, "Description");
                    if (m.IsTaxInvoice) HeaderCell(header, "HSN");
                    HeaderCell(header, "Qty", right: true);
                    HeaderCell(header, "Rate", right: true);
                    HeaderCell(header, "Amount", right: true);
                });

                foreach (var line in m.Lines)
                {
                    BodyCell(table, line.Description);
                    if (m.IsTaxInvoice) BodyCell(table, line.Hsn);
                    BodyCell(table, line.Quantity.ToString(), right: true);
                    BodyCell(table, Money(line.Rate), right: true);
                    BodyCell(table, Money(line.Amount), right: true);
                }
            });

            col.Item().PaddingTop(12).AlignRight().Width(170).Column(totals =>
            {
                TotalLine(totals, "Subtotal", Money(m.SubTotal));
                if (m.Discount > 0) TotalLine(totals, "Discount", $"−{Money(m.Discount)}", valueColor: ColTeal);
                // GST may be turned off for this tenant (§24) — omit the tax rows when there's no tax.
                if (m.CgstAmount > 0) TotalLine(totals, "CGST", Money(m.CgstAmount));
                if (m.SgstAmount > 0) TotalLine(totals, "SGST", Money(m.SgstAmount));

                totals.Item().PaddingTop(4).LineHorizontal(0.75f).LineColor(Colors.Grey.Lighten1);
                totals.Item().PaddingTop(3);
                TotalLine(totals, "Total", Money(m.TotalAmount), strong: true, size: 12, valueColor: Brand(m));

                // Show the paid/balance split ONLY on a partial payment. When fully paid, Total already
                // equals the amount and the header shows PAID, so a "Paid = Total" row is just noise;
                // when unpaid there's nothing to show.
                var balance = m.TotalAmount - m.PaidAmount;
                if (m.PaidAmount > 0 && balance > 0)
                {
                    TotalLine(totals, "Paid", Money(m.PaidAmount), valueColor: ColTeal);
                    TotalLine(totals, "Balance", Money(balance), valueColor: ColDanger);
                }
            });

            if (!string.IsNullOrWhiteSpace(m.Notes))
                col.Item().PaddingTop(12).Text($"Notes: {m.Notes}").FontSize(9).FontColor(Colors.Grey.Darken1);
        });
    }

    private static void HeaderCell(TableCellDescriptor header, string text, bool right = false)
    {
        var cell = header.Cell().Background(Colors.Grey.Lighten3).Padding(5);
        (right ? cell.AlignRight() : cell).Text(text).Bold().FontSize(9);
    }

    private static void BodyCell(TableDescriptor table, string text, bool right = false)
    {
        var cell = table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5);
        (right ? cell.AlignRight() : cell).Text(text).FontSize(9);
    }

    // Brand palette (matches the app's design tokens) — kept as hex so QuestPDF's string→Color
    // conversion is unambiguous in the totals block.
    private const string ColInk = "#444441";
    private const string ColInkMid = "#888780";
    private const string ColNavy = "#0C447C";
    private const string ColTeal = "#1D9E75";
    private const string ColDanger = "#E24B4A";

    /// <summary>One totals row: label on the left (grey), amount right-aligned. `strong` makes it the
    /// bold navy Total line; `valueColor` (brand hex) tints the amount (teal discount/paid, red balance due).</summary>
    private static void TotalLine(ColumnDescriptor col, string label, string value,
        bool strong = false, string? valueColor = null, float size = 10)
    {
        col.Item().PaddingVertical(1).Row(row =>
        {
            var lbl = row.RelativeItem().Text(label).FontSize(size).FontColor(strong ? ColInk : ColInkMid);
            if (strong) lbl.Bold();

            var val = row.RelativeItem().AlignRight().Text(value).FontSize(size)
                .FontColor(valueColor ?? (strong ? ColNavy : ColInk));
            if (strong) val.Bold();
        });
    }

    /// <summary>Colour-coded payment status shown in the header. Balance wins over status so a fully-settled
    /// invoice reads PAID regardless of its stored status; a cancelled invoice is always flagged.</summary>
    private static (string Text, string Color) StatusLabel(string status, decimal balanceDue) => status switch
    {
        "Cancelled" => ("CANCELLED", Colors.Red.Darken2),
        _ when balanceDue <= 0 => ("PAID", Colors.Green.Darken2),
        "Overdue" => ("OVERDUE", Colors.Red.Darken1),
        "PartiallyPaid" => ("PARTIALLY PAID", Colors.Orange.Darken2),
        _ => ("PAYMENT DUE", Colors.Orange.Darken1),
    };

    private static string Money(decimal value) => $"₹{value:N2}";

    /// <summary>The tenant's brand colour for accents (title, total), falling back to the app navy when
    /// unset or not a well-formed #RRGGBB hex — so a bad value can never break QuestPDF's colour parse.</summary>
    private static string Brand(InvoicePdfModel m) =>
        m.BrandColor is { Length: 7 } c && c[0] == '#' && c[1..].All(Uri.IsHexDigit) ? c : ColNavy;
}
