using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROCloud.API.Filters;
using ROCloud.Application.Common.Models;
using ROCloud.Application.Features.Orders.Commands.BulkCreateOrders;
using ROCloud.Application.Features.Orders.Commands.CancelOrder;
using ROCloud.Application.Features.Orders.Commands.CreateOrder;
using ROCloud.Application.Features.Orders.Commands.UpdateOrder;
using ROCloud.Application.Features.Orders.Dtos;
using ROCloud.Application.Features.Orders.Queries.GetOrderById;
using ROCloud.Application.Features.Orders.Queries.GetOrders;
using ROCloud.Application.Features.Orders.Queries.GetProductionPlan;
using ROCloud.Application.Features.Orders.Queries.GetUpcomingOrders;

namespace ROCloud.API.Controllers.Tenant;

[ApiController]
[Route("api/orders")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly IMediator _mediator;

    public OrdersController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [RequirePermission("Orders.View")]
    public async Task<IActionResult> GetOrders([FromQuery] OrderFilterDto filter, CancellationToken ct)
        => Ok(ApiResponse<PagedResult<OrderListItemDto>>.Ok(await _mediator.Send(new GetOrdersQuery(filter), ct)));

    /// <summary>Future-dated bookings (event/program orders) not yet on the day's delivery board.</summary>
    [HttpGet("upcoming")]
    [RequirePermission("Orders.View")]
    public async Task<IActionResult> GetUpcoming([FromQuery] int days, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<UpcomingOrderDto>>.Ok(
            await _mediator.Send(new GetUpcomingOrdersQuery(days <= 0 ? 60 : days), ct)));

    /// <summary>Per-day, per-product demand from upcoming bookings so the plant can prepare stock.</summary>
    [HttpGet("production-plan")]
    [RequirePermission("Orders.View")]
    public async Task<IActionResult> GetProductionPlan(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<ProductionPlanDayDto>>.Ok(
            await _mediator.Send(new GetProductionPlanQuery(from, to), ct)));

    [HttpGet("{id:guid}")]
    [RequirePermission("Orders.View")]
    public async Task<IActionResult> GetOrder(Guid id, CancellationToken ct)
        => Ok(ApiResponse<OrderDto>.Ok(await _mediator.Send(new GetOrderByIdQuery(id), ct)));

    [HttpPost]
    [RequirePermission("Orders.Create")]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderCommand command, CancellationToken ct)
    {
        var id = await _mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetOrder), new { id }, ApiResponse<object>.Ok(new { id }));
    }

    [HttpPost("bulk-from-subscriptions")]
    [RequirePermission("Orders.Create")]
    public async Task<IActionResult> BulkFromSubscriptions(
        [FromBody] BulkFromSubscriptionsRequest? body, CancellationToken ct)
        => Ok(ApiResponse<BulkCreateResultDto>.Ok(
            await _mediator.Send(new BulkCreateOrdersCommand(body?.TargetDate), ct)));

    [HttpPut("{id:guid}")]
    [RequirePermission("Orders.Edit")]
    public async Task<IActionResult> UpdateOrder(Guid id, [FromBody] UpdateOrderRequest body, CancellationToken ct)
    {
        await _mediator.Send(new UpdateOrderCommand(
            id, body.OrderDate, body.OrderType, body.Notes, body.Items, body.DeliveryMode), ct);
        return Ok(ApiResponse<object>.Ok(new { id }));
    }

    [HttpPost("{id:guid}/cancel")]
    [RequirePermission("Orders.Cancel")]
    public async Task<IActionResult> CancelOrder(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new CancelOrderCommand(id), ct);
        return Ok(ApiResponse<object>.Ok(new { id }));
    }
}

public sealed record BulkFromSubscriptionsRequest(DateOnly? TargetDate);

public sealed record UpdateOrderRequest(
    DateOnly? OrderDate, string? OrderType, string? Notes, IReadOnlyList<CreateOrderItemDto> Items,
    string? DeliveryMode = null);
