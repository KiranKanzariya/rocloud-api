using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROCloud.Application.Common.Models;
using ROCloud.Application.Features.Notifications.Commands.MarkNotificationsRead;
using ROCloud.Application.Features.Notifications.Dtos;
using ROCloud.Application.Features.Notifications.Queries.GetNotifications;

namespace ROCloud.API.Controllers.Tenant;

/// <summary>
/// The owner's in-app notification feed (guide §24) — the top-bar bell. Any authenticated tenant
/// user sees their own feed (scoped to their user id); no extra permission required. Alerts are
/// derived from the tenant's actionable state (overdue invoices, pending orders, AMC due, open
/// service requests).
/// </summary>
[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly IMediator _mediator;

    public NotificationsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
        => Ok(ApiResponse<NotificationFeedDto>.Ok(await _mediator.Send(new GetNotificationsQuery(), ct)));

    [HttpPost("read")]
    public async Task<IActionResult> MarkRead(CancellationToken ct)
        => Ok(ApiResponse<object>.Ok(new { marked = await _mediator.Send(new MarkNotificationsReadCommand(), ct) }));
}
