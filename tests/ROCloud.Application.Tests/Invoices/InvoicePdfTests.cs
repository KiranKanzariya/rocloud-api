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
            Notes: "Thank you for your business.",
            IsTaxInvoice: true, Status: "PartiallyPaid", PaidAmount: 300m, BrandColor: "#7A1FA2");

        AssertValidPdf(new InvoicePdfGenerator().Generate(model));
    }

    [Fact]
    public void Generate_BillOfSupply_NoGst_ProducesAValidPdf()
    {
        // GST off → bill of supply, no HSN column, no tax rows; fully paid → PAID marker.
        var model = new InvoicePdfModel(
            "INV-202606-0002",
            new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 16), null, null,
            "Small Water Co", null, null,
            "Sita Devi", "9876500000", null,
            [new InvoicePdfLine("20L Jar (20L)", "2201", 10, 40m, 400m)],
            SubTotal: 400m, CgstAmount: 0m, SgstAmount: 0m, Discount: 0m, TotalAmount: 400m,
            Notes: null,
            IsTaxInvoice: false, Status: "Paid", PaidAmount: 400m, BrandColor: null);

        AssertValidPdf(new InvoicePdfGenerator().Generate(model));
    }

    private static void AssertValidPdf(byte[] bytes)
    {
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 1000, "PDF looks too small to be real.");
        // PDF magic bytes: %PDF
        Assert.Equal(0x25, bytes[0]);
        Assert.Equal(0x50, bytes[1]);
        Assert.Equal(0x44, bytes[2]);
        Assert.Equal(0x46, bytes[3]);
    }
}
