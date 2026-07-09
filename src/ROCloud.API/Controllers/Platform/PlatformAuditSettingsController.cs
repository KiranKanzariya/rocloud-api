using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROCloud.API.Filters;
using ROCloud.Application.Common.Models;
using ROCloud.Application.Features.Platform.Audit.Commands.UpdateAuditSettings;
using ROCloud.Application.Features.Platform.Audit.Dtos;
using ROCloud.Application.Features.Platform.Audit.Queries.GetAuditSettings;

namespace ROCloud.API.Controllers.Platform;

/// <summary>Global activity-log configuration (guide §10.14). SuperAdmin only.</summary>
[ApiController]
[Route("api/platform/audit-settings")]
[Authorize]
[RequirePlatformRole("SuperAdmin")]
public class PlatformAuditSettingsController : ControllerBase
{
    private readonly IMediator _mediator;

    public PlatformAuditSettingsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
        => Ok(ApiResponse<AuditSettingsDto>.Ok(await _mediator.Send(new GetAuditSettingsQuery(), ct)));

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] UpdateAuditSettingsCommand command, CancellationToken ct)
        => Ok(ApiResponse<AuditSettingsDto>.Ok(await _mediator.Send(command, ct)));
}
