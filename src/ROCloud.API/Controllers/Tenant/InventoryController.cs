using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROCloud.API.Filters;
using ROCloud.Application.Common.Models;
using ROCloud.Application.Features.Inventory.Commands.AddInventoryMovement;
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

    [HttpPost("reconcile")]
    [RequirePermission("Inventory.Manage")]
    public async Task<IActionResult> Reconcile(CancellationToken ct)
        => Ok(ApiResponse<ReconcileResultDto>.Ok(await _mediator.Send(new ReconcileInventoryCommand(), ct)));
}
