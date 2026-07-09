using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Models;
using ROCloud.Application.Features.Platform.Billing.Dtos;

namespace ROCloud.Application.Features.Platform.Billing.Queries.GetBillingTransactions;

/// <summary>Platform billing transactions across all tenants, with totals.</summary>
public sealed record GetBillingTransactionsQuery(BillingFilterDto Filter) : IRequest<BillingPageDto>;

public class GetBillingTransactionsQueryHandler : IRequestHandler<GetBillingTransactionsQuery, BillingPageDto>
{
    private readonly IAppDbContext _db;

    public GetBillingTransactionsQueryHandler(IAppDbContext db) => _db = db;

    public async Task<BillingPageDto> Handle(GetBillingTransactionsQuery request, CancellationToken ct)
    {
        var f = request.Filter;
        var all = _db.PlatformBillingTransactions.AsQueryable();

        // Totals are over all transactions (not the filtered page).
        var totalRevenue = await all.Where(t => t.Status == "Paid").SumAsync(t => (decimal?)t.Amount, ct) ?? 0m;
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var thisMonth = await all.Where(t => t.Status == "Paid" && t.CreatedAt >= monthStart)
            .SumAsync(t => (decimal?)t.Amount, ct) ?? 0m;
        var failed = await all.CountAsync(t => t.Status == "Failed", ct);
        var refunded = await all.CountAsync(t => t.Status == "Refunded", ct);

        var query = all;
        if (!string.IsNullOrWhiteSpace(f.Status)) query = query.Where(t => t.Status == f.Status);
        if (f.TenantId is { } tid) query = query.Where(t => t.TenantId == tid);

        var total = await query.CountAsync(ct);
        var page = Math.Max(1, f.Page);
        var size = Math.Clamp(f.PageSize, 1, 100);

        var rows = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * size).Take(size)
            .Select(t => new BillingTransactionDto(
                t.Id, t.TenantId, t.Tenant!.Name, t.PlanType, t.Amount, t.BillingCycle,
                t.Status, t.RazorpayPaymentId, t.CreatedAt))
            .ToListAsync(ct);

        return new BillingPageDto(
            new PagedResult<BillingTransactionDto>(rows, total, page, size),
            totalRevenue, thisMonth, failed, refunded);
    }
}
