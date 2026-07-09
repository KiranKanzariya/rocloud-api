using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Platform.Dashboard.Dtos;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Platform.Dashboard.Queries.GetPlatformDashboard;

/// <summary>Platform KPIs: MRR/ARR, tenant counts by status, churn, and a 12-month revenue series.</summary>
public sealed record GetPlatformDashboardQuery : IRequest<PlatformDashboardDto>;

public class GetPlatformDashboardQueryHandler : IRequestHandler<GetPlatformDashboardQuery, PlatformDashboardDto>
{
    private readonly IAppDbContext _db;

    public GetPlatformDashboardQueryHandler(IAppDbContext db) => _db = db;

    public async Task<PlatformDashboardDto> Handle(GetPlatformDashboardQuery request, CancellationToken ct)
    {
        // Tenants is a platform table (not tenant-filtered). Soft-deleted tenants excluded.
        var tenants = await _db.Tenants.Include(t => t.Plan)
            .Where(t => !t.IsDeleted)
            .Select(t => new { t.Status, Price = t.Plan!.MonthlyPrice })
            .ToListAsync(ct);

        var total = tenants.Count;
        var active = tenants.Count(t => t.Status == TenantStatus.Active);
        var trial = tenants.Count(t => t.Status == TenantStatus.Trial);
        var suspended = tenants.Count(t => t.Status == TenantStatus.Suspended);
        var cancelled = tenants.Count(t => t.Status == TenantStatus.Cancelled);

        // MRR = recurring revenue from paying (Active) tenants.
        var mrr = tenants.Where(t => t.Status == TenantStatus.Active).Sum(t => t.Price);
        var churn = total > 0 ? Math.Round((double)cancelled / total * 100, 1) : 0;

        // 12-month revenue from Paid billing transactions.
        var since = DateTime.UtcNow.AddMonths(-11);
        var fromMonth = new DateTime(since.Year, since.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var paid = await _db.PlatformBillingTransactions
            .Where(b => b.Status == "Paid" && b.CreatedAt >= fromMonth)
            .Select(b => new { b.CreatedAt, b.Amount })
            .ToListAsync(ct);

        var byMonth = paid
            .GroupBy(b => new { b.CreatedAt.Year, b.CreatedAt.Month })
            .ToDictionary(g => (g.Key.Year, g.Key.Month), g => g.Sum(x => x.Amount));

        var series = new List<MonthlyRevenuePointDto>();
        for (var i = 0; i < 12; i++)
        {
            var d = fromMonth.AddMonths(i);
            series.Add(new MonthlyRevenuePointDto(d.Year, d.Month, byMonth.GetValueOrDefault((d.Year, d.Month), 0m)));
        }

        return new PlatformDashboardDto(mrr, mrr * 12, total, active, trial, suspended, cancelled, churn, series);
    }
}
