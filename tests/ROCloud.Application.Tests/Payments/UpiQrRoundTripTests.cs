using QRCoder;
using ROCloud.Application.Common;
using ROCloud.Infrastructure.Pdf;
using ZXing;
using ZXing.Common;

namespace ROCloud.Application.Tests.Payments;

/// <summary>
/// Proves a real decoder reads back exactly what we encoded.
///
/// <para>The other UPI tests assert the payload STRING is right and that the PDF renders. Neither
/// catches the failure that actually costs money: a payload that encodes into a QR a phone reads as
/// something else — a truncated VPA, a mangled amount — because the invoice would look perfect while
/// sending the customer's money to the wrong place, or asking for the wrong sum.</para>
///
/// <para>Decoding runs against the module matrix rather than the PNG, so no image-decoding dependency
/// is needed; the PNG step is QRCoder's own rendering and is covered by InvoicePdfTests. Crucially it
/// decodes at <see cref="UpiQrBlock.Ecc"/> — the level production uses — so raising or lowering error
/// correction can't pass here and ship differently.</para>
/// </summary>
public class UpiQrRoundTripTests
{
    private static string Decode(string payload)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, UpiQrBlock.Ecc);

        // QRCoder's matrix is row-major bits (true = dark). ZXing wants 8-bit luminance, so expand
        // each module to one pixel: dark → 0, light → 255.
        var matrix = data.ModuleMatrix;
        var size = matrix.Count;
        var luminance = new byte[size * size];
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
                luminance[(y * size) + x] = matrix[y][x] ? (byte)0 : (byte)255;

        var reader = new BarcodeReaderGeneric
        {
            Options = new DecodingOptions
            {
                PossibleFormats = [BarcodeFormat.QR_CODE],
                PureBarcode = true,
                TryHarder = true,
            },
        };

        var result = reader.Decode(new RGBLuminanceSource(
            luminance, size, size, RGBLuminanceSource.BitmapFormat.Gray8));

        Assert.NotNull(result);
        return result!.Text;
    }

    [Fact]
    public void ARealisticInvoicePayload_DecodesBackByteForByte()
    {
        var payload = UpiPaymentLink.Build(
            "dabhiro@okaxis", "Dabhi RO Water", 45m, "INV-202608-0012");

        Assert.Equal(payload, Decode(payload!));
    }

    [Fact]
    public void TheEscapedPayeeName_SurvivesEncoding()
    {
        // Percent-escapes and the reserved characters around them are where a naive encoder mangles
        // things: "%26" coming back as "&" would split the query and change where the money goes.
        var payload = UpiPaymentLink.Build("a.b-c_1@ybl", "A & B Waters", 1234.50m, "INV-1/2026");

        var decoded = Decode(payload!);
        Assert.Equal(payload, decoded);
        Assert.Contains("pn=A%20%26%20B%20Waters", decoded);
        Assert.Contains("am=1234.50", decoded);
    }

    [Fact]
    public void TheProductionEncoderReturnsAPng()
    {
        // Guards the other half: UpiQrBlock.Encode swallows exceptions and returns null, so a payload
        // it cannot encode would silently drop the QR from the invoice rather than fail loudly.
        var payload = UpiPaymentLink.Build("dabhiro@okaxis", "Dabhi RO Water", 45m, "INV-202608-0012");

        var png = UpiQrBlock.Encode(payload!);

        Assert.NotNull(png);
        Assert.NotEmpty(png!);
        // PNG magic number — proves it is an image QuestPDF can embed, not arbitrary bytes.
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png!.Take(4).ToArray());
    }
}
