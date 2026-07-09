using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROCloud.API.Filters;
using ROCloud.Application.Common.Models;
using ROCloud.Application.Features.Platform.Billing.Commands.RefundTransaction;
using ROCloud.Application.Features.Platform.Billing.Dtos;
using ROCloud.Application.Features.Platform.Billing.Queries.GetBillingTransactions;

namespace ROCloud.API.Controllers.Platform;

/// <summary>Platform billing dashboard (guide §26). SuperAdmin and Finance only.</summary>
[ApiController]
[Route("api/platform/billing")]
[Authorize]
[RequirePlatformRole("Finance")]
public class PlatformBillingController : ControllerBase
{
    private readonly IMediator _mediator;

    public PlatformBillingController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] BillingFilterDto filter, CancellationToken ct)
        => Ok(ApiResponse<BillingPageDto>.Ok(await _mediator.Send(new GetBillingTransactionsQuery(filter), ct)));

    [HttpPost("{id:guid}/refund")]
    public async Task<IActionResult> Refund(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new RefundTransactionCommand(id), ct);
        return Ok(ApiResponse<object>.Ok(new { id, status = "Refunded" }));
    }
}
