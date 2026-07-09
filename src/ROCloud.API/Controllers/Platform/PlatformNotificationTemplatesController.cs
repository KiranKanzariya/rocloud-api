using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROCloud.API.Filters;
using ROCloud.Application.Common.Models;
using ROCloud.Application.Features.NotificationTemplates.Dtos;
using ROCloud.Application.Features.Platform.NotificationTemplates.Commands.UpsertDefaultNotificationTemplate;
using ROCloud.Application.Features.Platform.NotificationTemplates.Queries.GetDefaultNotificationTemplates;

namespace ROCloud.API.Controllers.Platform;

/// <summary>
/// System-default notification templates (tenant_id IS NULL) — the shared baseline every tenant
/// inherits. Any platform role may read; only SuperAdmin may write, because a change here affects
/// every tenant that has not overridden the template.
/// </summary>
[ApiController]
[Route("api/platform/notification-templates")]
[Authorize]
[RequirePlatformRole]
public class PlatformNotificationTemplatesController : ControllerBase
{
    private readonly IMediator _mediator;

    public PlatformNotificationTemplatesController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? channel, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<NotificationTemplateDto>>.Ok(
            await _mediator.Send(new GetDefaultNotificationTemplatesQuery(channel), ct)));

    [HttpPut]
    [RequirePlatformRole("SuperAdmin")]
    public async Task<IActionResult> Upsert(
        [FromBody] UpsertDefaultNotificationTemplateCommand command, CancellationToken ct)
    {
        var id = await _mediator.Send(command, ct);
        return Ok(ApiResponse<object>.Ok(new { id }));
    }
}
