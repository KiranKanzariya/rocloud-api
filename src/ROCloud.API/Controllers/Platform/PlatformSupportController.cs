using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROCloud.API.Filters;
using ROCloud.Application.Common.Models;
using ROCloud.Application.Features.Platform.Support.Commands.AssignSupportTicket;
using ROCloud.Application.Features.Platform.Support.Commands.CreateSupportTicket;
using ROCloud.Application.Features.Platform.Support.Commands.UpdateSupportTicketStatus;
using ROCloud.Application.Features.Platform.Support.Dtos;
using ROCloud.Application.Features.Platform.Support.Queries.GetSupportTicketById;
using ROCloud.Application.Features.Platform.Support.Queries.GetSupportTickets;

namespace ROCloud.API.Controllers.Platform;

/// <summary>Cross-tenant support tickets (guide §26). Any platform staff view; Support/SuperAdmin manage.</summary>
[ApiController]
[Route("api/platform/support")]
[Authorize]
[RequirePlatformRole]
public class PlatformSupportController : ControllerBase
{
    private readonly IMediator _mediator;

    public PlatformSupportController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] SupportFilterDto filter, CancellationToken ct)
        => Ok(ApiResponse<PagedResult<SupportTicketListItemDto>>.Ok(
            await _mediator.Send(new GetSupportTicketsQuery(filter), ct)));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        => Ok(ApiResponse<SupportTicketDto>.Ok(await _mediator.Send(new GetSupportTicketByIdQuery(id), ct)));

    [HttpPost]
    [RequirePlatformRole("Support")]
    public async Task<IActionResult> Create([FromBody] CreateSupportTicketCommand command, CancellationToken ct)
    {
        var id = await _mediator.Send(command, ct);
        return Ok(ApiResponse<object>.Ok(new { id }));
    }

    [HttpPut("{id:guid}/assign")]
    [RequirePlatformRole("Support")]
    public async Task<IActionResult> Assign(Guid id, [FromBody] AssignTicketRequest body, CancellationToken ct)
    {
        await _mediator.Send(new AssignSupportTicketCommand(id, body.PlatformUserId), ct);
        return Ok(ApiResponse<object>.Ok(new { id }));
    }

    [HttpPatch("{id:guid}/status")]
    [RequirePlatformRole("Support")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateTicketStatusRequest body, CancellationToken ct)
    {
        await _mediator.Send(new UpdateSupportTicketStatusCommand(id, body.Status, body.ResolutionNote), ct);
        return Ok(ApiResponse<object>.Ok(new { id }));
    }
}

public sealed record AssignTicketRequest(Guid PlatformUserId);
public sealed record UpdateTicketStatusRequest(string Status, string? ResolutionNote);
