using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROCloud.API.Filters;
using ROCloud.Application.Common.Models;
using ROCloud.Application.Features.Platform.Billing.Commands.RefundTransaction;
using ROCloud.Application.Features.Platform.Billing.Dtos;
using ROCloud.Application.Features.Platform.Billing.Queries.GetBillingInvoice;
using ROCloud.Application.Features.Platform.Billing.Queries.GetBillingInvoicePdf;
using ROCloud.Application.Features.Platform.Billing.Queries.GetBillingTransactions;
using ROCloud.Application.Features.Subscription.Dtos;

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

    /// <summary>The subscription invoice this transaction paid for, as data. 404 when none is linked.</summary>
    [HttpGet("{id:guid}/invoice")]
    public async Task<IActionResult> Invoice(Guid id, CancellationToken ct)
        => Ok(ApiResponse<SubscriptionInvoiceDto>.Ok(await _mediator.Send(new GetBillingInvoiceQuery(id), ct)));

    /// <summary>The subscription-invoice PDF this transaction paid for. 404 when no invoice is linked.</summary>
    [HttpGet("{id:guid}/invoice/pdf")]
    public async Task<IActionResult> InvoicePdf(Guid id, CancellationToken ct)
    {
        var pdf = await _mediator.Send(new GetBillingInvoicePdfQuery(id), ct);
        return File(pdf.Bytes, "application/pdf", pdf.FileName);
    }

    [HttpPost("{id:guid}/refund")]
    public async Task<IActionResult> Refund(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new RefundTransactionCommand(id), ct);
        return Ok(ApiResponse<object>.Ok(new { id, status = "Refunded" }));
    }
}
