using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROCloud.API.Filters;
using ROCloud.Application.Features.Roles.Commands.CreateRole;
using ROCloud.Application.Features.Roles.Commands.DeleteRole;
using ROCloud.Application.Features.Roles.Commands.UpdateRolePermissions;
using ROCloud.Application.Features.Roles.Queries.GetPermissions;
using ROCloud.Application.Features.Roles.Queries.GetRoles;

namespace ROCloud.API.Controllers.Tenant;

[ApiController]
[Route("api/roles")]
[Authorize]
public class RolesController : ControllerBase
{
    private readonly IMediator _mediator;

    public RolesController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [RequirePermission("Roles.Manage")]
    public async Task<IActionResult> GetRoles(CancellationToken ct)
        => Ok(await _mediator.Send(new GetRolesQuery(), ct));

    [HttpGet("permissions")]
    [RequirePermission("Roles.Manage")]
    public async Task<IActionResult> GetPermissions(CancellationToken ct)
        => Ok(await _mediator.Send(new GetPermissionsQuery(), ct));

    [HttpPost]
    [RequirePermission("Roles.Manage")]
    // Plan gating is the CustomRolesEnabled flag, enforced in the command handler (not a fixed tier).
    public async Task<IActionResult> Create([FromBody] CreateRoleRequest body, CancellationToken ct)
    {
        var id = await _mediator.Send(new CreateRoleCommand(body.Name, body.Permissions ?? []), ct);
        return CreatedAtAction(nameof(GetRoles), new { id }, new { id });
    }

    [HttpPut("{id:guid}/permissions")]
    [RequirePermission("Roles.Manage")]
    public async Task<IActionResult> UpdatePermissions(Guid id, [FromBody] UpdateRolePermissionsRequest body, CancellationToken ct)
    {
        await _mediator.Send(new UpdateRolePermissionsCommand(id, body.Permissions ?? []), ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission("Roles.Manage")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeleteRoleCommand(id), ct);
        return NoContent();
    }
}

public sealed record CreateRoleRequest(string Name, string[]? Permissions);
public sealed record UpdateRolePermissionsRequest(string[]? Permissions);
