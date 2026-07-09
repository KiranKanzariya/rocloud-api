namespace ROCloud.Application.Features.Platform.Dashboard.Dtos;

/// <summary>Platform-wide KPIs for the super-admin dashboard (guide §26).</summary>
public sealed record PlatformDashboardDto(
    decimal Mrr,
    decimal Arr,
    int TotalTenants,
    int ActiveTenants,
    int TrialTenants,
    int SuspendedTenants,
    int CancelledTenants,
    double ChurnRatePct,
    IReadOnlyList<MonthlyRevenuePointDto> MonthlyRevenue);

/// <summary>One month of collected platform revenue (from billing transactions).</summary>
public sealed record MonthlyRevenuePointDto(int Year, int Month, decimal Revenue);
