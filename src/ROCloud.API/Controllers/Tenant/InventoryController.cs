using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROCloud.API.Filters;
using ROCloud.Application.Common.Models;
using ROCloud.Application.Features.Inventory.Commands.AddInventoryMovement;
using ROCloud.Application.Features.Inventory.Commands.RecordCustomerReturn;
using ROCloud.Application.Features.Inventory.Commands.ReconcileInventory;
using ROCloud.Application.Features.Inventory.Dtos;
using ROCloud.Application.Features.Inventory.Queries.GetInventory;
using ROCloud.Application.Features.Inventory.Queries.GetInventoryByProduct;
using ROCloud.Application.Features.Inventory.Queries.GetInventoryMovements;

namespace ROCloud.API.Controllers.Tenant;

[ApiController]
[Route("api/inventory")]
[Authorize]
public class InventoryController : ControllerBase
{
    private readonly IMediator _mediator;

    public InventoryController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [RequirePermission("Inventory.View")]
    public async Task<IActionResult> GetInventory(CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<InventorySummaryDto>>.Ok(await _mediator.Send(new GetInventoryQuery(), ct)));

    [HttpGet("{productId:guid}")]
    [RequirePermission("Inventory.View")]
    public async Task<IActionResult> GetInventoryByProduct(Guid productId, CancellationToken ct)
        => Ok(ApiResponse<InventorySummaryDto>.Ok(
            await _mediator.Send(new GetInventoryByProductQuery(productId), ct)));

    [HttpGet("movements")]
    [RequirePermission("Inventory.View")]
    public async Task<IActionResult> GetMovements([FromQuery] InventoryMovementFilterDto filter, CancellationToken ct)
        => Ok(ApiResponse<PagedResult<InventoryMovementDto>>.Ok(
            await _mediator.Send(new GetInventoryMovementsQuery(filter), ct)));

    [HttpPost("movements")]
    [RequirePermission("Inventory.Manage")]
    public async Task<IActionResult> AddMovement([FromBody] AddInventoryMovementCommand command, CancellationToken ct)
    {
        var id = await _mediator.Send(command, ct);
        return Ok(ApiResponse<object>.Ok(new { id }));
    }

    /// <summary>
    /// Records empty jars a customer handed back with no delivery to attach them to (moved house, missed
    /// on the day). May be backdated within the platform window. Reduces the customer's outstanding jars.
    ///
    /// <para>Optionally records money handed over at the same moment — the counter case, where the jars
    /// and the cash are one event. That half additionally requires Payments.Collect, checked in the
    /// handler.</para>
    /// </summary>
    [HttpPost("returns")]
    [RequirePermission("Inventory.Manage")]
    public async Task<IActionResult> RecordCustomerReturn(
        [FromBody] RecordCustomerReturnCommand command, CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        // `id` stays for the existing callers; the payment is reported alongside it.
        return Ok(ApiResponse<object>.Ok(new
        {
            id = result.MovementId,
            paymentId = result.PaymentId,
            collectedAmount = result.CollectedAmount,
        }));
    }

    [HttpPost("reconcile")]
    [RequirePermission("Inventory.Manage")]
    public async Task<IActionResult> Reconcile(CancellationToken ct)
        => Ok(ApiResponse<ReconcileResultDto>.Ok(await _mediator.Send(new ReconcileInventoryCommand(), ct)));
}
