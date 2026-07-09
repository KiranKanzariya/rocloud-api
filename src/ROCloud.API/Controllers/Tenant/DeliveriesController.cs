using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROCloud.API.Filters;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Models;
using ROCloud.Application.Features.Deliveries.Commands.SetDeliveryProof;
using ROCloud.Application.Features.Deliveries.Commands.UpdateDeliveryStatus;
using ROCloud.Application.Features.Deliveries.Dtos;
using ROCloud.Application.Features.Deliveries.Queries.GetDeliveries;
using ROCloud.Application.Features.Deliveries.Queries.GetDeliveryBoard;
using ROCloud.Application.Features.Deliveries.Queries.GetDeliveryDetail;
using ROCloud.Application.Features.Deliveries.Queries.GetDeliverySummary;
using ROCloud.Application.Features.Deliveries.Queries.GetMyRoute;
using ROCloud.Application.Features.Deliveries.Services;

namespace ROCloud.API.Controllers.Tenant;

[ApiController]
[Route("api/deliveries")]
[Authorize]
public class DeliveriesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IDeliveryProofService _proofService;
    private readonly ITenantContext _tenant;

    public DeliveriesController(IMediator mediator, IDeliveryProofService proofService, ITenantContext tenant)
    {
        _mediator = mediator;
        _proofService = proofService;
        _tenant = tenant;
    }

    [HttpGet]
    [RequirePermission("Deliveries.View")]
    public async Task<IActionResult> GetDeliveries([FromQuery] DeliveryFilterDto filter, CancellationToken ct)
        => Ok(ApiResponse<PagedResult<DeliveryListItemDto>>.Ok(
            await _mediator.Send(new GetDeliveriesQuery(filter), ct)));

    [HttpGet("board")]
    [RequirePermission("Deliveries.View")]
    public async Task<IActionResult> GetBoard([FromQuery] DeliveryFilterDto filter, CancellationToken ct)
        => Ok(ApiResponse<DeliveryBoardDto>.Ok(await _mediator.Send(new GetDeliveryBoardQuery(filter), ct)));

    [HttpGet("summary")]
    [RequirePermission("Deliveries.View")]
    public async Task<IActionResult> GetSummary([FromQuery] DeliveryFilterDto filter, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<DeliverySummaryRowDto>>.Ok(
            await _mediator.Send(new GetDeliverySummaryQuery(filter), ct)));

    [HttpGet("my-route")]
    [RequirePermission("Deliveries.ViewOwn")]
    public async Task<IActionResult> GetMyRoute([FromQuery] DeliveryFilterDto filter, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<DeliveryListItemDto>>.Ok(
            await _mediator.Send(new GetMyRouteQuery(filter), ct)));

    /// <summary>What was recorded at a completed stop — for the read-only delivery summary.</summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("Deliveries.View")]
    public async Task<IActionResult> GetDetail(Guid id, CancellationToken ct)
        => Ok(ApiResponse<DeliveryDetailDto>.Ok(await _mediator.Send(new GetDeliveryDetailQuery(id), ct)));

    [HttpPatch("{id:guid}/status")]
    [RequirePermission("Deliveries.Update")]
    public async Task<IActionResult> UpdateStatus(
        Guid id, [FromBody] UpdateDeliveryStatusDto body, CancellationToken ct)
    {
        await _mediator.Send(new UpdateDeliveryStatusCommand(
            id, body.Status, body.JarsDelivered, body.JarsReturned, body.CollectedAmount,
            body.PaymentMethod, body.ProofImageUrl, body.Latitude, body.Longitude, body.Notes,
            body.Items, body.OtherReturns), ct);
        return Ok(ApiResponse<object>.Ok(new { id }));
    }

    [HttpPost("{id:guid}/proof")]
    [RequirePermission("Deliveries.Update")]
    [RequestSizeLimit(6 * 1024 * 1024)]   // a touch above the 5 MB service limit
    public async Task<IActionResult> UploadProof(Guid id, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(ApiResponse<object>.Fail("No file was uploaded.", "file"));

        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);

        var path = await _proofService.SaveAsync(
            ms.ToArray(), file.FileName, file.ContentType, _tenant.TenantId, ct);

        var stored = await _mediator.Send(new SetDeliveryProofCommand(id, path), ct);
        return Ok(ApiResponse<object>.Ok(new { id, proofImageUrl = stored }));
    }
}
