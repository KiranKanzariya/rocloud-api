using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROCloud.API.Filters;
using ROCloud.Application.Common.Models;
using ROCloud.Application.Features.ServiceRequests.Commands.AssignTechnician;
using ROCloud.Application.Features.ServiceRequests.Commands.CreateServiceRequest;
using ROCloud.Application.Features.ServiceRequests.Commands.ScheduleAmcVisits;
using ROCloud.Application.Features.ServiceRequests.Commands.UpdateServiceStatus;
using ROCloud.Application.Features.ServiceRequests.Dtos;
using ROCloud.Application.Features.ServiceRequests.Queries.GetMyServiceJobs;
using ROCloud.Application.Features.ServiceRequests.Queries.GetServiceRequestById;
using ROCloud.Application.Features.ServiceRequests.Queries.GetServiceRequests;

namespace ROCloud.API.Controllers.Tenant;

[ApiController]
[Route("api/service-requests")]
[Authorize]
public class ServiceRequestsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ServiceRequestsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [RequirePermission("AMC.View")]
    public async Task<IActionResult> GetServiceRequests(
        [FromQuery] ServiceRequestFilterDto filter, CancellationToken ct)
        => Ok(ApiResponse<PagedResult<ServiceRequestListItemDto>>.Ok(
            await _mediator.Send(new GetServiceRequestsQuery(filter), ct)));

    [HttpGet("{id:guid}")]
    [RequirePermission("AMC.View")]
    public async Task<IActionResult> GetServiceRequest(Guid id, CancellationToken ct)
        => Ok(ApiResponse<ServiceRequestDto>.Ok(await _mediator.Send(new GetServiceRequestByIdQuery(id), ct)));

    [HttpGet("mine")]
    [RequirePermission("AMC.Update")]
    public async Task<IActionResult> GetMyJobs([FromQuery] string? status, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<ServiceRequestListItemDto>>.Ok(
            await _mediator.Send(new GetMyServiceJobsQuery(status), ct)));

    [HttpPost]
    [RequirePermission("AMC.Manage")]
    public async Task<IActionResult> Create([FromBody] CreateServiceRequestCommand command, CancellationToken ct)
    {
        var id = await _mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetServiceRequest), new { id }, ApiResponse<object>.Ok(new { id }));
    }

    [HttpPut("{id:guid}/assign")]
    [RequirePermission("AMC.Manage")]
    public async Task<IActionResult> Assign(Guid id, [FromBody] AssignTechnicianRequest body, CancellationToken ct)
    {
        await _mediator.Send(new AssignTechnicianCommand(id, body.TechnicianId), ct);
        return Ok(ApiResponse<object>.Ok(new { id }));
    }

    [HttpPatch("{id:guid}/status")]
    [RequirePermission("AMC.Update")]
    public async Task<IActionResult> UpdateStatus(
        Guid id, [FromBody] UpdateServiceStatusRequest body, CancellationToken ct)
    {
        await _mediator.Send(new UpdateServiceStatusCommand(id, body.Status, body.ResolutionNotes), ct);
        return Ok(ApiResponse<object>.Ok(new { id }));
    }

    [HttpPost("schedule-amc")]
    [RequirePermission("AMC.Manage")]
    public async Task<IActionResult> ScheduleAmc([FromBody] ScheduleAmcVisitsCommand command, CancellationToken ct)
        => Ok(ApiResponse<AmcScheduleResultDto>.Ok(await _mediator.Send(command, ct)));
}

public sealed record AssignTechnicianRequest(Guid TechnicianId);

public sealed record UpdateServiceStatusRequest(string Status, string? ResolutionNotes);
