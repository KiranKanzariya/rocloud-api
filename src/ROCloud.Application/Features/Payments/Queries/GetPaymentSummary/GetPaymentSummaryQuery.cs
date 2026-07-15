using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Payments.Dtos;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Payments.Queries.GetPaymentSummary;

/// <summary>
/// What the tenant actually COLLECTED in a date window, split by method — summed in SQL over every
/// matching row.
///
/// The payments page and the dashboard used to compute these tiles in the browser by fetching a page
/// of payments and reducing it. That silently under-reported real money: the list endpoint clamps
/// pageSize to 100, so any window with more than 100 payments simply lost the rest. Money on screen
/// must never depend on a page size — hence a real aggregate.
///
/// Only Completed payments count: a Pending online checkout that was never finished is not collection.
/// </summary>
public sealed record GetPaymentSummaryQuery(DateOnly? FromDate, DateOnly? ToDate)
    : IRequest<PaymentSummaryDto>;

public class GetPaymentSummaryQueryHandler : IRequestHandler<GetPaymentSummaryQuery, PaymentSummaryDto>
{
    private readonly IAppDbContext _db;

    public GetPaymentSummaryQueryHandler(IAppDbContext db) => _db = db;

    public async Task<PaymentSummaryDto> Handle(GetPaymentSummaryQuery request, CancellationToken ct)
    {
        var query = _db.Payments.Where(p => p.Status == PaymentStatus.Completed);

        // Same window semantics as GetPaymentsQuery, so the tiles agree with the table beneath them.
        if (request.FromDate is { } from)
        {
            var fromTs = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(p => p.PaidAt >= fromTs);
        }
        if (request.ToDate is { } to)
        {
            var toTs = to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            query = query.Where(p => p.PaidAt <= toTs);
        }

        var byMethod = await query
            .GroupBy(p => p.PaymentMethod)
            .Select(g => new
            {
                Method = g.Key,
                Amount = g.Sum(x => (decimal?)x.Amount) ?? 0m,
                Count = g.Count()
            })
            .ToListAsync(ct);

        var cash = byMethod.Where(x => x.Method == PaymentMethod.Cash).Sum(x => x.Amount);
        var upi = byMethod.Where(x => x.Method == PaymentMethod.UPI).Sum(x => x.Amount);
        var collected = byMethod.Sum(x => x.Amount);

        return new PaymentSummaryDto(
            Collected: collected,
            Count: byMethod.Sum(x => x.Count),
            Cash: cash,
            Upi: upi,
            Other: collected - cash - upi);
    }
}
