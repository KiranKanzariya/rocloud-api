using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
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

        var rows = await _db.Invoices
            .Where(i => UnpaidStatuses.Contains(i.Status)
                        && i.TotalAmount - i.PaidAmount > 0
                        && i.DueDate <= cutoff)
            .GroupBy(i => new { i.CustomerId, Name = i.Customer!.Name, i.Customer.Mobile, i.Customer.Email, i.Customer.PreferredLanguage })
            .Select(g => new
            {
                g.Key.CustomerId,
                g.Key.Name,
                g.Key.Mobile,
                g.Key.Email,
                g.Key.PreferredLanguage,
                InvoiceCount = g.Count(),
                Outstanding = g.Sum(i => i.TotalAmount - i.PaidAmount),
                OldestDueDate = g.Min(i => i.DueDate)
            })
            .ToListAsync(ct);

        return rows
            .Select(r => new OutstandingDueDto(
                r.CustomerId, r.Name, r.Mobile, r.InvoiceCount, r.Outstanding,
                r.OldestDueDate, today.DayNumber - r.OldestDueDate.DayNumber, r.PreferredLanguage, r.Email))
            .OrderByDescending(r => r.OutstandingAmount)
            .ToList();
    }
}
