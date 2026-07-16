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
                row.RelativeItem().AlignMiddle().Text("INVOICE").FontSize(16).Bold().FontColor(ColNavy);
                // ROCloud brand lock-up: the logo mark + wordmark, in brand navy.
                row.ConstantItem(130).AlignRight().Row(brand =>
                {
                    brand.ConstantItem(22).Height(22).Svg(RocloudLogoSvg);
                    brand.AutoItem().PaddingLeft(5).AlignMiddle().Text("ROCloud").Bold().FontSize(14).FontColor(ColNavy);
                });
            });
            col.Item().PaddingTop(4).Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text("ROCloud").Bold().FontColor(ColNavy);
                    left.Item().Text("RO business management platform").FontSize(9).FontColor(Colors.Grey.Darken1);
                });
                row.ConstantItem(200).Column(right =>
                {
                    right.Item().AlignRight().Text($"Invoice #: {m.InvoiceNumber}").Bold();
                    right.Item().AlignRight().Text($"Date: {m.InvoiceDate:dd MMM yyyy}").FontSize(9);
                    right.Item().AlignRight().Text($"Due: {m.PeriodStart:dd MMM yyyy}").FontSize(9);
                    right.Item().AlignRight().Text($"Period: {m.PeriodStart:dd MMM} – {m.PeriodEnd:dd MMM yyyy}").FontSize(9);

                    right.Item().AlignRight().PaddingTop(3).Text(m.Paid ? "PAID" : "PAYMENT DUE")
                        .Bold().FontColor(m.Paid ? ColTeal : ColAmber);
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
                    HeaderCell(header, "Amount", right: true);
                });

                BodyCell(table, m.LineDescription);
                BodyCell(table, m.BillingCycle);
                BodyCell(table, Money(m.GrossAmount), right: true);
            });

            col.Item().PaddingTop(12).AlignRight().Width(170).Column(totals =>
            {
                TotalLine(totals, "Subtotal", Money(m.GrossAmount));
                if (m.DiscountAmount > 0)
                    TotalLine(totals, "Discount", $"−{Money(m.DiscountAmount)}", valueColor: ColTeal);

                totals.Item().PaddingTop(4).LineHorizontal(0.75f).LineColor(Colors.Grey.Lighten1);
                totals.Item().PaddingTop(3);
                TotalLine(totals, "Total", Money(m.Amount), strong: true, size: 12);
            });
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
    private const string ColAmber = "#EF9F27";

    // The canonical ROCloud brand mark (matches roc-logo in the portals) — rendered inline as SVG.
    private const string RocloudLogoSvg =
        """
        <svg viewBox="0 0 44 44" fill="none" xmlns="http://www.w3.org/2000/svg">
          <rect width="44" height="44" rx="10" fill="#0C447C"/>
          <rect x="8" y="26" width="28" height="10" rx="5" fill="#185FA5"/>
          <rect x="12" y="22" width="20" height="10" rx="5" fill="#378ADD"/>
          <rect x="15" y="18" width="14" height="10" rx="5" fill="#B5D4F4"/>
          <circle cx="22" cy="15" r="6" fill="#1D9E75"/>
          <path d="M20 15L21.5 16.5L24.5 13" stroke="#E1F5EE" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/>
        </svg>
        """;

    /// <summary>One totals row: label on the left (grey), amount right-aligned. `strong` makes it the
    /// bold navy Total line; `valueColor` (brand hex) tints the amount (teal discount/paid).</summary>
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

    private static string Money(decimal value) => $"₹{value:N2}";
}
