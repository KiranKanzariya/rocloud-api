using ROCloud.Application.Features.Invoices.Dtos;
using ROCloud.Infrastructure.Pdf;

namespace ROCloud.Application.Tests.Invoices;

public class InvoicePdfTests
{
    [Fact]
    public void Generate_ProducesAValidPdf()
    {
        var model = new InvoicePdfModel(
            "INV-202606-0001",
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 16),
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            "Acme RO Waters", "29ABCDE1234F1Z5", "12 MG Road, Bengaluru, KA, 560001",
            "Ravi Kumar", "9876543210", null,
            [new InvoicePdfLine("20L Jar (20L)", "2201", 15, 40m, 600m)],
            SubTotal: 600m, CgstAmount: 54m, SgstAmount: 54m, Discount: 0m, TotalAmount: 708m,
            Notes: "Thank you for your business.");

        var bytes = new InvoicePdfGenerator().Generate(model);

        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 1000, "PDF looks too small to be real.");
        // PDF magic bytes: %PDF
        Assert.Equal(0x25, bytes[0]);
        Assert.Equal(0x50, bytes[1]);
        Assert.Equal(0x44, bytes[2]);
        Assert.Equal(0x46, bytes[3]);
    }
}
