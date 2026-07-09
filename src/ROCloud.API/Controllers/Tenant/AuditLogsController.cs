using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROCloud.API.Filters;
using ROCloud.Application.Common.Models;
using ROCloud.Application.Features.AuditLogs.Queries.ExportAuditLogs;
using ROCloud.Application.Features.AuditLogs.Queries.GetAuditAvailability;
using ROCloud.Application.Features.AuditLogs.Queries.GetAuditLogs;

namespace ROCloud.API.Controllers.Tenant;

[ApiController]
[Route("api/audit-logs")]
[Authorize]
public class AuditLogsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AuditLogsController(IMediator mediator) => _mediator = mediator;

    /// <summary>Whether the activity log is enabled — used to show/hide the owner's menu &amp; page.</summary>
    [HttpGet("availability")]
    public async Task<IActionResult> Availability(CancellationToken ct)
        => Ok(ApiResponse<AuditAvailabilityDto>.Ok(await _mediator.Send(new GetAuditAvailabilityQuery(), ct)));

    [HttpGet]
    [RequireOwner]
    public async Task<IActionResult> GetAuditLogs([FromQuery] AuditLogFilterDto filter, CancellationToken ct)
        => Ok(ApiResponse<PagedResult<AuditLogDto>>.Ok(await _mediator.Send(new GetAuditLogsQuery(filter), ct)));

    [HttpGet("export")]
    [RequireOwner]
    public async Task<IActionResult> Export([FromQuery] AuditLogFilterDto filter, CancellationToken ct)
    {
        var csv = await _mediator.Send(new ExportAuditLogsQuery(filter), ct);
        return File(csv, "text/csv", "activity-log.csv");
    }
}
