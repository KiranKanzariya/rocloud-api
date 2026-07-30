using ROCloud.Application.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Invoices;
using ROCloud.Application.Features.Payments.Dtos;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Payments.Queries.GetOutstandingDues;

/// <summary>
/// Customers who OWE money and have an obligation aged past <see cref="OverdueDays"/>. The amount is the
/// full customer ledger — billed − paid — which is what the customer page shows and includes uninvoiced
/// delivered orders. A per-bottle / weekly tenant is never auto-invoiced, so an invoice-only definition
/// reported them as ₹0 outstanding even while they owed; this counts those dues too.
/// </summary>
public sealed record GetOutstandingDuesQuery(int OverdueDays = 7) : IRequest<IReadOnlyList<OutstandingDueDto>>;

public class GetOutstandingDuesQueryHandler
    : IRequestHandler<GetOutstandingDuesQuery, IReadOnlyList<OutstandingDueDto>>
{
    private static readonly InvoiceStatus[] UnpaidStatuses =
        [InvoiceStatus.Sent, InvoiceStatus.PartiallyPaid, InvoiceStatus.Overdue];

    private readonly IAppDbContext _db;

    public GetOutstandingDuesQueryHandler(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<OutstandingDueDto>> Handle(
        GetOutstandingDuesQuery request, CancellationToken ct)
    {
        var today = AppTimeZone.Today(DateTime.UtcNow);
        var cutoff = today.AddDays(-Math.Max(0, request.OverdueDays));

        // A delivered order line is "uninvoiced" when no non-cancelled invoice period covers its date
        // (mirrors CustomerBalance / GetCustomers). Candidates: customers with an AGED unpaid obligation
        // — an unpaid invoice due on/before the cutoff, OR an uninvoiced delivered order that old. The
        // full balance is then computed for each; only those who genuinely owe (> 0 after payments,
        // including unallocated advances) survive.
        var candidates = await _db.Customers
            .Where(c =>
                _db.Invoices.Any(i => i.CustomerId == c.Id && UnpaidStatuses.Contains(i.Status)
                    && i.TotalAmount - i.PaidAmount > 0 && i.DueDate <= cutoff)
                || _db.OrderItems.Any(oi => oi.Order!.CustomerId == c.Id
                    && oi.Order.Status == OrderStatus.Delivered && oi.Order.OrderDate <= cutoff
                    && !_db.Invoices.Any(inv => inv.CustomerId == c.Id && inv.Status != InvoiceStatus.Cancelled
                        && inv.PeriodFrom != null && inv.PeriodTo != null
                        && oi.Order.OrderDate >= inv.PeriodFrom && oi.Order.OrderDate <= inv.PeriodTo)))
            .Select(c => new
            {
                c.Id, c.Name, c.Mobile, c.Email, c.PreferredLanguage,
                // Balance = billed − paid (identical to CustomerBalance.ComputeAsync / GetCustomers).
                OwedInvoices = _db.Invoices.Where(i => i.CustomerId == c.Id && i.Status != InvoiceStatus.Cancelled)
                    .Sum(i => (decimal?)i.TotalAmount) ?? 0m,
                OwedUninvoiced = _db.OrderItems.Where(oi => oi.Order!.CustomerId == c.Id
                        && oi.Order.Status == OrderStatus.Delivered
                        && !_db.Invoices.Any(inv => inv.CustomerId == c.Id && inv.Status != InvoiceStatus.Cancelled
                            && inv.PeriodFrom != null && inv.PeriodTo != null
                            && oi.Order.OrderDate >= inv.PeriodFrom && oi.Order.OrderDate <= inv.PeriodTo))
                    .Sum(oi => (decimal?)(oi.Quantity * oi.UnitRate)) ?? 0m,
                Paid = _db.Payments.Where(p => p.CustomerId == c.Id && p.Status == PaymentStatus.Completed)
                    .Sum(p => (decimal?)p.Amount) ?? 0m,
                UnpaidInvoiceCount = _db.Invoices.Count(i => i.CustomerId == c.Id
                    && UnpaidStatuses.Contains(i.Status) && i.TotalAmount - i.PaidAmount > 0),
                OldestInvoiceDue = _db.Invoices.Where(i => i.CustomerId == c.Id
                        && UnpaidStatuses.Contains(i.Status) && i.TotalAmount - i.PaidAmount > 0)
                    .Min(i => (DateOnly?)i.DueDate),
                OldestUninvoicedOrder = _db.OrderItems.Where(oi => oi.Order!.CustomerId == c.Id
                        && oi.Order.Status == OrderStatus.Delivered
                        && !_db.Invoices.Any(inv => inv.CustomerId == c.Id && inv.Status != InvoiceStatus.Cancelled
                            && inv.PeriodFrom != null && inv.PeriodTo != null
                            && oi.Order.OrderDate >= inv.PeriodFrom && oi.Order.OrderDate <= inv.PeriodTo))
                    .Min(oi => (DateOnly?)oi.Order!.OrderDate)
            })
            .ToListAsync(ct);

        return candidates
            .Select(c => new { c, Balance = c.OwedInvoices + c.OwedUninvoiced - c.Paid, Oldest = MinDate(c.OldestInvoiceDue, c.OldestUninvoicedOrder) })
            .Where(x => x.Balance > 0m && x.Oldest is not null)
            .Select(x => new OutstandingDueDto(
                x.c.Id, x.c.Name, x.c.Mobile, x.c.UnpaidInvoiceCount, x.Balance,
                x.Oldest!.Value, today.DayNumber - x.Oldest.Value.DayNumber, x.c.PreferredLanguage, x.c.Email))
            .OrderByDescending(r => r.OutstandingAmount)
            .ToList();
    }

    /// <summary>The earlier of two nullable dates (null = "no date on this side").</summary>
    private static DateOnly? MinDate(DateOnly? a, DateOnly? b) =>
        a is null ? b : b is null ? a : (a.Value <= b.Value ? a : b);
}
