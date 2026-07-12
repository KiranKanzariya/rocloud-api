using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROCloud.API.Filters;
using ROCloud.Application.Common.Models;
using ROCloud.Application.Features.Areas.Commands.CreateArea;
using ROCloud.Application.Features.Areas.Commands.DeleteArea;
using ROCloud.Application.Features.Areas.Commands.UpdateArea;
using ROCloud.Application.Features.Areas.Dtos;
using ROCloud.Application.Features.Areas.Queries.GetAreas;

namespace ROCloud.API.Controllers.Tenant;

/// <summary>
/// Delivery areas / zones (guide §24). Reads need Areas.View; writes need Areas.Manage.
/// </summary>
[ApiController]
[Route("api/areas")]
[Authorize]
public class AreasController : ControllerBase
{
    private readonly IMediator _mediator;

    public AreasController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [RequirePermission("Areas.View")]
    public async Task<IActionResult> GetAreas([FromQuery] bool includeInactive, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<AreaDto>>.Ok(await _mediator.Send(new GetAreasQuery(includeInactive), ct)));

    [HttpPost]
    [RequirePermission("Areas.Manage")]
    public async Task<IActionResult> Create([FromBody] CreateAreaCommand command, CancellationToken ct)
    {
        var id = await _mediator.Send(command, ct);
        return Ok(ApiResponse<object>.Ok(new { id }));
    }

    [HttpPut("{id:guid}")]
    [RequirePermission("Areas.Manage")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAreaRequest body, CancellationToken ct)
    {
        await _mediator.Send(new UpdateAreaCommand(id, body.Name, body.City, body.Pincode, body.IsActive), ct);
        return Ok(ApiResponse<object>.Ok(new { id }));
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission("Areas.Manage")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeleteAreaCommand(id), ct);
        return Ok(ApiResponse<object>.Ok(new { id }));
    }
}

public sealed record UpdateAreaRequest(string Name, string? City, string? Pincode, bool IsActive);
