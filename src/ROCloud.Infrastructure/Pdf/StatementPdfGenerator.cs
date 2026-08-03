using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Statements.Dtos;

namespace ROCloud.Infrastructure.Pdf;

/// <summary>
/// Delivery-statement renderer: proof of what was supplied, day by day. Deliberately NOT invoice-shaped
/// — no invoice number, no discount, no GST, no balance, and a footer that disclaims tax-invoice status.
/// A second document that reads like a tax invoice for the same supply is a real problem for a
/// GST-registered tenant, so those omissions are the point, not an oversight.
///
/// Kept independent of InvoicePdfGenerator (which is itself independent of SubscriptionInvoicePdfGenerator):
/// the few shared helpers are cheaper to repeat than a shared base that couples three documents whose
/// layouts drift apart.
/// </summary>
public class StatementPdfGenerator : IStatementPdfGenerator
{
    static StatementPdfGenerator() => QuestPDF.Settings.License = LicenseType.Community;

    public byte[] Generate(StatementPdfModel m)
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
                    .Text(t =>
                    {
                        t.Span($"Computer-generated delivery statement from {m.BusinessName}.  ")
                            .FontSize(8).FontColor(Colors.Grey.Medium);
                        t.Span("Page ").FontSize(8).FontColor(Colors.Grey.Medium);
                        t.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Medium);
                        t.Span(" of ").FontSize(8).FontColor(Colors.Grey.Medium);
                        t.TotalPages().FontSize(8).FontColor(Colors.Grey.Medium);
                    });
            });
        });

        return document.GeneratePdf();
    }

    private static void Header(IContainer container, StatementPdfModel m)
    {
        container.Column(col =>
        {
            col.Item().Text("DELIVERY STATEMENT").FontSize(16).Bold().FontColor(Brand(m));
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
                    right.Item().AlignRight()
                        .Text($"Period: {m.PeriodFrom:dd MMM yyyy} – {m.PeriodTo:dd MMM yyyy}").Bold();
                    right.Item().AlignRight().Text($"Issued: {m.IssuedOn:dd MMM yyyy}").FontSize(9);
                    // No document number: a statement must never look like it belongs to the invoice series.
                    right.Item().AlignRight().PaddingTop(3)
                        .Text("NOT A TAX INVOICE").FontSize(9).Bold().FontColor(Colors.Grey.Darken1);
                });
            });
            col.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
        });
    }

    private static void Content(IContainer container, StatementPdfModel m)
    {
        container.PaddingVertical(10).Column(col =>
        {
            col.Item().Text("Supplied To").FontSize(9).FontColor(Colors.Grey.Darken1);
            col.Item().Text(m.CustomerName).Bold();
            var idLine = string.Join("  ·  ",
                new[] { m.CustomerCode, m.CustomerMobile }.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (!string.IsNullOrWhiteSpace(idLine)) col.Item().Text(idLine).FontSize(9);
            if (m.CustomerAddress is not null) col.Item().Text(m.CustomerAddress).FontSize(9);

            col.Item().PaddingTop(10).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(1.5f);   // date
                    c.RelativeColumn(3.5f);   // item
                    c.RelativeColumn(1.2f);   // delivered
                    c.RelativeColumn(1.2f);   // returned
                    c.RelativeColumn(1.2f);   // rate
                    c.RelativeColumn(1.5f);   // amount
                });

                table.Header(header =>
                {
                    HeaderCell(header, "Date");
                    HeaderCell(header, "Item");
                    HeaderCell(header, "Delivered", right: true);
                    HeaderCell(header, "Returned", right: true);
                    HeaderCell(header, "Rate", right: true);
                    HeaderCell(header, "Amount", right: true);
                });

                foreach (var line in m.Lines)
                {
                    BodyCell(table, $"{line.Date:dd MMM yyyy}");
                    BodyCell(table, line.Description);
                    BodyCell(table, line.Delivered.ToString(), right: true);
                    BodyCell(table, line.Returned.ToString(), right: true);
                    BodyCell(table, Money(line.Rate), right: true);
                    BodyCell(table, Money(line.Amount), right: true);
                }
            });

            col.Item().PaddingTop(8).Row(row =>
            {
                row.RelativeItem().Column(totals =>
                {
                    totals.Item().Text($"Total delivered: {m.TotalDelivered} jars").Bold().FontSize(10);
                    totals.Item().Text($"Total returned: {m.TotalReturned} jars")
                        .FontSize(9).FontColor(ColInkMid);
                });
                row.ConstantItem(190).AlignRight().Column(totals =>
                {
                    // Gross value of what was supplied — before any customer discount and before GST.
                    // The invoice is the document that prices this; showing a discounted or taxed figure
                    // here would invite it to be treated as a claimable bill.
                    totals.Item().AlignRight().Text($"Value of goods supplied: {Money(m.TotalAmount)}")
                        .Bold().FontColor(Brand(m));
                    totals.Item().AlignRight().Text("Before discount and tax")
                        .FontSize(8).FontColor(ColInkMid);
                });
            });

            if (m.ProductTotals.Count > 0)
            {
                col.Item().PaddingTop(10).Text("Summary by item").FontSize(9).FontColor(Colors.Grey.Darken1);
                foreach (var t in m.ProductTotals)
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Text(t.Description).FontSize(9);
                        row.ConstantItem(120).AlignRight().Text($"{t.Delivered} jars").FontSize(9);
                    });
            }

            if (m.StandaloneReturns.Count > 0)
            {
                col.Item().PaddingTop(12)
                    .Text("Empty jars returned separately").FontSize(9).FontColor(Colors.Grey.Darken1);
                foreach (var r in m.StandaloneReturns)
                    col.Item().Row(row =>
                    {
                        row.ConstantItem(90).Text($"{r.Date:dd MMM yyyy}").FontSize(9);
                        row.RelativeItem().Text(r.Description + (r.Damaged ? " — damaged" : "")).FontSize(9);
                        row.ConstantItem(80).AlignRight().Text($"{r.Quantity} jars").FontSize(9);
                    });
            }

            col.Item().PaddingTop(14).LineHorizontal(0.75f).LineColor(Colors.Grey.Lighten1);
            col.Item().PaddingTop(6).Text(BillingNote(m)).FontSize(9).FontColor(ColInk);
            col.Item().PaddingTop(2).Text(
                    "This statement is a record of supply, not a demand for payment, and is not a tax invoice.")
                .FontSize(8).FontColor(Colors.Grey.Darken1);

            col.Item().PaddingTop(24).Row(row =>
            {
                row.RelativeItem();
                row.ConstantItem(200).Column(sign =>
                {
                    sign.Item().LineHorizontal(0.75f).LineColor(Colors.Grey.Medium);
                    sign.Item().PaddingTop(3).AlignCenter()
                        .Text("Authorised signatory").FontSize(8).FontColor(ColInkMid);
                });
            });
        });
    }

    /// <summary>
    /// Points at the tax document(s) that bill these deliveries — the thing a customer's employer needs
    /// alongside proof of supply. Says only what is true: fully billed, partly billed, or not yet.
    /// </summary>
    private static string BillingNote(StatementPdfModel m)
    {
        var numbers = string.Join(", ", m.InvoiceNumbers);

        if (m.InvoiceNumbers.Count == 0)
            return "These deliveries have not been invoiced yet.";

        var label = m.InvoiceNumbers.Count == 1 ? "invoice" : "invoices";
        return m.UninvoicedOrderCount == 0
            ? $"These deliveries are billed on {label} {numbers}."
            : $"Partly billed on {label} {numbers}; {m.UninvoicedOrderCount} item(s) listed here are not invoiced yet.";
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

    private const string ColInk = "#444441";
    private const string ColInkMid = "#888780";
    private const string ColNavy = "#0C447C";

    private static string Money(decimal value) => $"₹{value:N2}";

    /// <summary>Tenant brand colour for accents, falling back to app navy when unset or malformed.</summary>
    private static string Brand(StatementPdfModel m) =>
        m.BrandColor is { Length: 7 } c && c[0] == '#' && c[1..].All(Uri.IsHexDigit) ? c : ColNavy;
}
