using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROCloud.API.Filters;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Models;
using ROCloud.Domain.Enums;

namespace ROCloud.API.Controllers.Tenant;

/// <summary>
/// Reporting endpoints backed by raw ADO.NET (guide §9b/§12). Reports are a Pro/Enterprise
/// feature and require the Reports.View permission. The repository scopes every query by the
/// current tenant; the controller passes ITenantContext.TenantId.
/// </summary>
[ApiController]
[Route("api/reports")]
[Authorize]
[RequirePlan(PlanType.Pro)]
[RequirePermission("Reports.View")]
public class ReportsController : ControllerBase
{
    private readonly IReportRepository _reports;
    private readonly IReportExporter _exporter;
    private readonly ITenantContext _tenant;

    public ReportsController(IReportRepository reports, IReportExporter exporter, ITenantContext tenant)
    {
        _reports = reports;
        _exporter = exporter;
        _tenant = tenant;
    }

    private Guid TenantId => _tenant.TenantId;
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    private static DateOnly MonthStart => new(Today.Year, Today.Month, 1);

    [HttpGet("collection")]
    public async Task<IActionResult> Collection(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
        => Ok(ApiResponse<object>.Ok(
            await _reports.GetDailyCollectionAsync(TenantId, from ?? MonthStart, to ?? Today, ct)));

    [HttpGet("monthly-collection")]
    public async Task<IActionResult> MonthlyCollection([FromQuery] int? year, CancellationToken ct)
        => Ok(ApiResponse<object>.Ok(
            await _reports.GetMonthlyCollectionAsync(TenantId, year ?? Today.Year, ct)));

    [HttpGet("delivery-efficiency")]
    public async Task<IActionResult> DeliveryEfficiency([FromQuery] DateOnly? date, CancellationToken ct)
        => Ok(ApiResponse<object>.Ok(
            await _reports.GetDeliveryEfficiencyAsync(TenantId, date ?? Today, ct)));

    [HttpGet("outstanding-dues")]
    public async Task<IActionResult> OutstandingDues([FromQuery] DateOnly? asOf, CancellationToken ct)
        => Ok(ApiResponse<object>.Ok(
            await _reports.GetOutstandingDuesAsync(TenantId, asOf ?? Today, ct)));

    [HttpGet("area-performance")]
    public async Task<IActionResult> AreaPerformance(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
        => Ok(ApiResponse<object>.Ok(
            await _reports.GetAreaWisePerformanceAsync(TenantId, from ?? MonthStart, to ?? Today, ct)));

    [HttpGet("top-customers")]
    public async Task<IActionResult> TopCustomers(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] int? limit, CancellationToken ct)
        => Ok(ApiResponse<object>.Ok(
            await _reports.GetTopCustomersAsync(TenantId, from ?? MonthStart, to ?? Today, limit ?? 20, ct)));

    [HttpGet("bottle-tracking")]
    public async Task<IActionResult> BottleTracking(CancellationToken ct)
        => Ok(ApiResponse<object>.Ok(await _reports.GetBottleTrackingReportAsync(TenantId, ct)));

    [HttpGet("customer-acquisition")]
    public async Task<IActionResult> CustomerAcquisition([FromQuery] int? year, CancellationToken ct)
        => Ok(ApiResponse<object>.Ok(
            await _reports.GetCustomerAcquisitionAsync(TenantId, year ?? Today.Year, ct)));

    [HttpGet("{report}/export")]
    public async Task<IActionResult> Export(
        string report,
        [FromQuery] string format,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] DateOnly? date, [FromQuery] DateOnly? asOf,
        [FromQuery] int? year, [FromQuery] int? limit,
        CancellationToken ct)
    {
        var f = from ?? MonthStart;
        var t = to ?? Today;

        return report.ToLowerInvariant() switch
        {
            "collection" => File(await _reports.GetDailyCollectionAsync(TenantId, f, t, ct)),
            "monthly-collection" => File(await _reports.GetMonthlyCollectionAsync(TenantId, year ?? Today.Year, ct)),
            "delivery-efficiency" => File(await _reports.GetDeliveryEfficiencyAsync(TenantId, date ?? Today, ct)),
            "outstanding-dues" => File(await _reports.GetOutstandingDuesAsync(TenantId, asOf ?? Today, ct)),
            "area-performance" => File(await _reports.GetAreaWisePerformanceAsync(TenantId, f, t, ct)),
            "top-customers" => File(await _reports.GetTopCustomersAsync(TenantId, f, t, limit ?? 20, ct)),
            "bottle-tracking" => File(await _reports.GetBottleTrackingReportAsync(TenantId, ct)),
            "customer-acquisition" => File(await _reports.GetCustomerAcquisitionAsync(TenantId, year ?? Today.Year, ct)),
            _ => NotFound(ApiResponse<object>.Fail("Unknown report.", "report"))
        };

        // Local helper: serialise typed rows to the requested format and return as a download.
        FileContentResult File<T>(T[] rows)
        {
            var isXlsx = string.Equals(format, "xlsx", StringComparison.OrdinalIgnoreCase);
            return isXlsx
                ? base.File(_exporter.ToXlsx(rows, report), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{report}.xlsx")
                : base.File(_exporter.ToCsv(rows), "text/csv", $"{report}.csv");
        }
    }
}
