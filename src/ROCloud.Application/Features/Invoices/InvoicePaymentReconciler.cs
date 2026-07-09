using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Invoices;

/// <summary>
/// Credits a freshly-generated invoice with payments already collected against the orders it bills
/// (e.g. a PerBottle customer who paid at the door). Without this, the invoice would re-bill orders
/// the customer has already settled (guide §9). The payments are attributed to the invoice so they
/// leave the unallocated pool, and the invoice's paid amount / status reflect them.
/// </summary>
public static class InvoicePaymentReconciler
{
    public static async Task CreditPriorPaymentsAsync(
        IAppDbContext db, Invoice invoice, Guid customerId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var orderIds = await db.Orders
            .Where(o => o.CustomerId == customerId
                && o.Status == OrderStatus.Delivered
                && o.OrderDate >= from && o.OrderDate <= to)
            .Select(o => o.Id)
            .ToListAsync(ct);
        if (orderIds.Count == 0) return;

        var priorPayments = await db.Payments
            .Where(p => p.Status == PaymentStatus.Completed
                && p.InvoiceId == null
                && p.OrderId != null && orderIds.Contains(p.OrderId.Value))
            .ToListAsync(ct);
        if (priorPayments.Count == 0) return;

        foreach (var p in priorPayments)
            p.InvoiceId = invoice.Id;

        invoice.PaidAmount = Math.Min(priorPayments.Sum(p => p.Amount), invoice.TotalAmount);
        invoice.Status = invoice.PaidAmount >= invoice.TotalAmount && invoice.TotalAmount > 0m
            ? InvoiceStatus.Paid
            : invoice.PaidAmount > 0m ? InvoiceStatus.PartiallyPaid : InvoiceStatus.Draft;
    }
}
