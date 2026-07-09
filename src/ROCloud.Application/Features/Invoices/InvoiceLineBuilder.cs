using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Invoices.Dtos;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Invoices;

/// <summary>
/// Builds invoice line items from a customer's Delivered orders in a period, grouped by
/// product. There is no invoice_items table, so lines are reconstructed on demand from the
/// underlying orders. Shared by GenerateInvoice (subtotal), the detail query, and the PDF.
/// </summary>
internal static class InvoiceLineBuilder
{
    public static async Task<List<InvoiceLineItemDto>> BuildAsync(
        IAppDbContext db, Guid customerId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        // Group by product AND unit rate, so a product delivered at two prices in the period (e.g. a
        // mid-period price change) yields one clean line per rate — each showing the exact rate charged
        // — instead of a single line with a confusing blended average. The subtotal is identical either way.
        var rows = await db.OrderItems
            .Where(i => i.Order != null
                        && i.Order.CustomerId == customerId
                        && i.Order.Status == OrderStatus.Delivered
                        && i.Order.OrderDate >= from
                        && i.Order.OrderDate <= to)
            .GroupBy(i => new { i.ProductId, ProductName = i.Product!.Name, i.Product.BottleSize, i.UnitRate })
            .Select(g => new
            {
                g.Key.ProductName,
                g.Key.BottleSize,
                g.Key.UnitRate,
                Quantity = g.Sum(x => x.Quantity),
                Amount = g.Sum(x => x.Quantity * x.UnitRate)
            })
            .ToListAsync(ct);

        return rows
            .OrderBy(r => r.ProductName).ThenBy(r => r.UnitRate)
            .Select(r => new InvoiceLineItemDto(
                r.ProductName,
                r.BottleSize.ToWire(),
                r.Quantity,
                r.UnitRate,
                r.Amount))
            .ToList();
    }
}
