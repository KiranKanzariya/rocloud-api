using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ROCloud.Infrastructure.Pdf;

/// <summary>
/// The scan-to-pay block shared by the invoice and statement PDFs (guide §10): a UPI QR, the amount
/// it asks for, and the payee id in plain text.
///
/// <para>
/// <b>PngByteQRCode, never QRCode.</b> QRCoder's <c>QRCode</c> type renders through
/// System.Drawing.Common, which throws on Linux — and this API runs in a Linux container on Render, so
/// that choice would fail in production while working perfectly on a Windows dev box. PngByteQRCode
/// has no such dependency and hands back bytes QuestPDF's Image() takes directly.
/// </para>
///
/// <para>
/// The VPA is printed BESIDE the QR on purpose. A customer must be able to see where their money is
/// going before they send it, and still pay if the scan fails — nothing in ROCloud can verify that the
/// id is real or belongs to this tenant.
/// </para>
/// </summary>
internal static class UpiQrBlock
{
    private const float QrSize = 96f;
    private const string ColInkMid = "#888780";

    /// <summary>
    /// Q (25% recovery) rather than the usual M: these are printed, photographed off a screen, and
    /// scanned in poor light at a plant counter, and the payload is short enough that the redundancy
    /// costs no meaningful size. Exposed so the round-trip test decodes at the level production uses —
    /// otherwise the test could pass at one ECC while the invoice ships another.
    /// </summary>
    internal const QRCodeGenerator.ECCLevel Ecc = QRCodeGenerator.ECCLevel.Q;

    /// <summary>
    /// Draws the block, or nothing at all when <paramref name="payload"/> is null — which is how a
    /// settled invoice, or a tenant with no UPI id configured, ends up with no QR.
    /// </summary>
    public static void Render(ColumnDescriptor col, string? payload, string? vpa, string amountLabel)
    {
        if (string.IsNullOrWhiteSpace(payload)) return;

        var png = Encode(payload);
        if (png is null) return;   // never let a QR failure take the whole invoice down

        col.Item().PaddingTop(12).Row(row =>
        {
            row.ConstantItem(QrSize).Image(png).FitArea();
            row.ConstantItem(10);
            row.RelativeItem().PaddingTop(6).Column(text =>
            {
                text.Item().Text("Scan to pay").Bold().FontSize(10);
                text.Item().Text(amountLabel).FontSize(9).FontColor(ColInkMid);
                if (!string.IsNullOrWhiteSpace(vpa))
                    text.Item().PaddingTop(2).Text($"UPI ID: {vpa}").FontSize(9).FontColor(ColInkMid);
                text.Item().PaddingTop(2)
                    .Text("Pay using any UPI app (GPay, PhonePe, Paytm, BHIM).")
                    .FontSize(8).FontColor(Colors.Grey.Medium);
            });
        });
    }

    /// <summary>
    /// PNG bytes for the payload. Returns null rather than throwing: a customer's invoice must still
    /// render — minus the QR — if encoding ever fails, because the document is also an accounting record.
    /// </summary>
    internal static byte[]? Encode(string payload)
    {
        try
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(payload, Ecc);
            return new PngByteQRCode(data).GetGraphic(pixelsPerModule: 10);
        }
        catch
        {
            return null;
        }
    }
}
