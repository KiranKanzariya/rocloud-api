using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROCloud.API.Filters;
using ROCloud.Application.Common.Models;
using ROCloud.Application.Features.AmcSubscriptions.Commands.CancelAmcSubscription;
using ROCloud.Application.Features.AmcSubscriptions.Commands.CreateAmcSubscription;
using ROCloud.Application.Features.AmcSubscriptions.Commands.UpdateAmcSubscription;
using ROCloud.Application.Features.AmcSubscriptions.Dtos;
using ROCloud.Application.Features.AmcSubscriptions.Queries.GetAmcSubscriptionById;
using ROCloud.Application.Features.AmcSubscriptions.Queries.GetAmcSubscriptions;

namespace ROCloud.API.Controllers.Tenant;

[ApiController]
[Route("api/amc-subscriptions")]
[Authorize]
public class AmcSubscriptionsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AmcSubscriptionsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [RequirePermission("AMC.View")]
    public async Task<IActionResult> GetSubscriptions(
        [FromQuery] AmcSubscriptionFilterDto filter, CancellationToken ct)
        => Ok(ApiResponse<PagedResult<AmcSubscriptionListItemDto>>.Ok(
            await _mediator.Send(new GetAmcSubscriptionsQuery(filter), ct)));

    [HttpGet("{id:guid}")]
    [RequirePermission("AMC.View")]
    public async Task<IActionResult> GetSubscription(Guid id, CancellationToken ct)
        => Ok(ApiResponse<AmcSubscriptionListItemDto>.Ok(
            await _mediator.Send(new GetAmcSubscriptionByIdQuery(id), ct)));

    [HttpPost]
    [RequirePermission("AMC.Manage")]
    public async Task<IActionResult> Create([FromBody] CreateAmcSubscriptionCommand command, CancellationToken ct)
    {
        var id = await _mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetSubscription), new { id }, ApiResponse<object>.Ok(new { id }));
    }

    [HttpPut("{id:guid}")]
    [RequirePermission("AMC.Manage")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAmcSubscriptionRequest body, CancellationToken ct)
    {
        await _mediator.Send(new UpdateAmcSubscriptionCommand(
            id, body.PlanName, body.IntervalMonths, body.Amount, body.EndDate,
            body.NextDueDate, body.LastServiceDate, body.IsActive), ct);
        return Ok(ApiResponse<object>.Ok(new { id }));
    }

    [HttpPost("{id:guid}/cancel")]
    [RequirePermission("AMC.Manage")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new CancelAmcSubscriptionCommand(id), ct);
        return Ok(ApiResponse<object>.Ok(new { id }));
    }
}

public sealed record UpdateAmcSubscriptionRequest(
    string? PlanName, int IntervalMonths, decimal Amount, DateOnly? EndDate,
    DateOnly NextDueDate, DateOnly? LastServiceDate, bool IsActive);
