using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROCloud.API.Filters;
using ROCloud.Application.Common.Models;
using ROCloud.Application.Features.NotificationTemplates.Commands.DeleteNotificationTemplate;
using ROCloud.Application.Features.NotificationTemplates.Commands.UpsertNotificationTemplate;
using ROCloud.Application.Features.NotificationTemplates.Dtos;
using ROCloud.Application.Features.NotificationTemplates.Queries.GetNotificationTemplates;

namespace ROCloud.API.Controllers.Tenant;

/// <summary>
/// Per-tenant notification templates (guide §24): Email/SMS/WhatsApp message bodies. Reads need
/// Settings.View; writes need Settings.Manage. WhatsApp is a Pro+ feature, gated in the portal UI.
/// </summary>
[ApiController]
[Route("api/notification-templates")]
[Authorize]
public class NotificationTemplatesController : ControllerBase
{
    private readonly IMediator _mediator;

    public NotificationTemplatesController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [RequirePermission("Settings.View")]
    public async Task<IActionResult> Get([FromQuery] string? channel, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<NotificationTemplateDto>>.Ok(
            await _mediator.Send(new GetNotificationTemplatesQuery(channel), ct)));

    [HttpPut]
    [RequirePermission("Settings.Manage")]
    public async Task<IActionResult> Upsert([FromBody] UpsertNotificationTemplateCommand command, CancellationToken ct)
    {
        var id = await _mediator.Send(command, ct);
        return Ok(ApiResponse<object>.Ok(new { id }));
    }

    /// <summary>Delete the tenant's own override, reverting the template to the system default.</summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("Settings.Manage")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeleteNotificationTemplateCommand(id), ct);
        return NoContent();
    }
}
