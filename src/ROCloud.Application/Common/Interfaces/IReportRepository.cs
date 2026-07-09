using ROCloud.Application.Features.Reports.Dtos;

namespace ROCloud.Application.Common.Interfaces;

/// <summary>
/// Reporting reads implemented with raw ADO.NET (Npgsql) for performance on large tables
/// (guide §9b, §12). Every query is parameterised and scoped by tenant_id — there is no EF
/// global query filter here, so the implementation enforces tenant isolation explicitly.
/// </summary>
public interface IReportRepository
{
    Task<DailyCollectionDto[]> GetDailyCollectionAsync(Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct);
    Task<MonthlyCollectionDto[]> GetMonthlyCollectionAsync(Guid tenantId, int year, CancellationToken ct);
    Task<DeliveryEfficiencyDto[]> GetDeliveryEfficiencyAsync(Guid tenantId, DateOnly date, CancellationToken ct);
    Task<OutstandingDuesReportDto[]> GetOutstandingDuesAsync(Guid tenantId, DateOnly asOfDate, CancellationToken ct);
    Task<AreaPerformanceDto[]> GetAreaWisePerformanceAsync(Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct);
    Task<TopCustomerDto[]> GetTopCustomersAsync(Guid tenantId, DateOnly from, DateOnly to, int limit, CancellationToken ct);
    Task<BottleTrackingReportDto[]> GetBottleTrackingReportAsync(Guid tenantId, CancellationToken ct);
    Task<CustomerAcquisitionDto[]> GetCustomerAcquisitionAsync(Guid tenantId, int year, CancellationToken ct);
}
