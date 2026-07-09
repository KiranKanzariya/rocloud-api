using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROCloud.API.Filters;
using ROCloud.Application.Common.Models;
using ROCloud.Application.Features.Platform.Users.Commands.CreatePlatformUser;
using ROCloud.Application.Features.Platform.Users.Commands.UpdatePlatformUser;
using ROCloud.Application.Features.Platform.Users.Dtos;
using ROCloud.Application.Features.Platform.Users.Queries.GetPlatformUsers;

namespace ROCloud.API.Controllers.Platform;

/// <summary>Platform staff management (guide §26). SuperAdmin only.</summary>
[ApiController]
[Route("api/platform/users")]
[Authorize]
[RequirePlatformRole("SuperAdmin")]
public class PlatformUsersController : ControllerBase
{
    private readonly IMediator _mediator;

    public PlatformUsersController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<PlatformUserDto>>.Ok(await _mediator.Send(new GetPlatformUsersQuery(), ct)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePlatformUserCommand command, CancellationToken ct)
    {
        var id = await _mediator.Send(command, ct);
        return Ok(ApiResponse<object>.Ok(new { id }));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePlatformUserRequest body, CancellationToken ct)
    {
        await _mediator.Send(new UpdatePlatformUserCommand(id, body.Name, body.PlatformRole, body.IsActive), ct);
        return Ok(ApiResponse<object>.Ok(new { id }));
    }
}

public sealed record UpdatePlatformUserRequest(string Name, string PlatformRole, bool IsActive);
