using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROCloud.API.Filters;
using ROCloud.Application.Common.Models;
using ROCloud.Application.Features.Users.Commands.CreateUser;
using ROCloud.Application.Features.Users.Commands.DeactivateUser;
using ROCloud.Application.Features.Users.Commands.InviteUser;
using ROCloud.Application.Features.Users.Commands.ResetUserPassword;
using ROCloud.Application.Features.Users.Commands.UpdateUser;
using ROCloud.Application.Features.Users.Dtos;
using ROCloud.Application.Features.Users.Queries.GetUserById;
using ROCloud.Application.Features.Users.Queries.GetUsers;

namespace ROCloud.API.Controllers.Tenant;

[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly IMediator _mediator;

    public UsersController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [RequirePermission("Users.View")]
    public async Task<IActionResult> GetUsers([FromQuery] UserFilterDto filter, CancellationToken ct)
        => Ok(ApiResponse<PagedResult<UserListItemDto>>.Ok(await _mediator.Send(new GetUsersQuery(filter), ct)));

    [HttpGet("{id:guid}")]
    [RequirePermission("Users.View")]
    public async Task<IActionResult> GetUser(Guid id, CancellationToken ct)
        => Ok(ApiResponse<UserDto>.Ok(await _mediator.Send(new GetUserByIdQuery(id), ct)));

    [HttpPost]
    [RequirePermission("Users.Manage")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserCommand command, CancellationToken ct)
    {
        var id = await _mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetUser), new { id }, ApiResponse<object>.Ok(new { id }));
    }

    [HttpPut("{id:guid}")]
    [RequirePermission("Users.Manage")]
    public async Task<IActionResult> UpdateUser(Guid id, [FromBody] UpdateUserRequest body, CancellationToken ct)
    {
        await _mediator.Send(new UpdateUserCommand(
            id, body.Name, body.Mobile, body.RoleId, body.IsActive, body.AreaIds), ct);
        return Ok(ApiResponse<object>.Ok(new { id }));
    }

    [HttpPost("{id:guid}/deactivate")]
    [RequirePermission("Users.Manage")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeactivateUserCommand(id), ct);
        return Ok(ApiResponse<object>.Ok(new { id }));
    }

    [HttpPost("{id:guid}/reset-password")]
    [RequirePermission("Users.Manage")]
    public async Task<IActionResult> ResetPassword(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new ResetUserPasswordCommand(id), ct);
        return Ok(ApiResponse<object>.Ok(new { id }));
    }

    [HttpPost("invite")]
    [RequirePermission("Users.Manage")]
    public async Task<IActionResult> Invite([FromBody] InviteUserCommand command, CancellationToken ct)
    {
        var id = await _mediator.Send(command, ct);
        return Ok(ApiResponse<object>.Ok(new { id }));
    }
}

public sealed record UpdateUserRequest(
    string Name, string? Mobile, Guid RoleId, bool IsActive, IReadOnlyList<Guid>? AreaIds);
