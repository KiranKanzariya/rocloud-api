using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Invoices;
using ROCloud.Application.Features.Payments.Dtos;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Payments.Queries.GetOutstandingDues;

/// <summary>Customers with unpaid invoices whose due date is more than <see cref="OverdueDays"/> ago.</summary>
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
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var cutoff = today.AddDays(-Math.Max(0, request.OverdueDays));

        // Candidates by what the ROW says. An invoice can still be settled by a payment the owner
        // recorded against the customer rather than against the invoice, so this is not the answer yet.
        var candidates = await _db.Invoices
            .Where(i => UnpaidStatuses.Contains(i.Status)
                        && i.TotalAmount - i.PaidAmount > 0
                        && i.DueDate <= cutoff)
            .Select(i => new
            {
                i.Id,
                i.CustomerId,
                Name = i.Customer!.Name,
                i.Customer.Mobile,
                i.Customer.Email,
                i.Customer.PreferredLanguage,
                i.TotalAmount,
                i.PaidAmount,
                i.Status,
                i.DueDate
            })
            .ToListAsync(ct);

        if (candidates.Count == 0) return [];

        // …so drain each customer's unallocated payment pool over their obligations and keep only the
        // invoices that are STILL owed. Without this the reminder chases money already in the till —
        // e.g. an imported opening balance the owner settled from the customer page.
        var allocations = (await CustomerObligationAllocator.ComputeAsync(
            _db, candidates.Select(c => c.CustomerId).Distinct().ToList(), ct)).Invoices;

        var unpaid = candidates
            .Select(c => new
            {
                c.CustomerId, c.Name, c.Mobile, c.Email, c.PreferredLanguage, c.DueDate,
                Balance = InvoicePaymentStatus.Resolve(
                    c.Status, c.TotalAmount, c.PaidAmount, allocations.GetValueOrDefault(c.Id, 0m)).Balance
            })
            .Where(c => c.Balance > 0m);

        return unpaid
            .GroupBy(c => new { c.CustomerId, c.Name, c.Mobile, c.Email, c.PreferredLanguage })
            .Select(g =>
            {
                var oldest = g.Min(x => x.DueDate);
                return new OutstandingDueDto(
                    g.Key.CustomerId, g.Key.Name, g.Key.Mobile, g.Count(), g.Sum(x => x.Balance),
                    oldest, today.DayNumber - oldest.DayNumber, g.Key.PreferredLanguage, g.Key.Email);
            })
            .OrderByDescending(r => r.OutstandingAmount)
            .ToList();
    }
}
