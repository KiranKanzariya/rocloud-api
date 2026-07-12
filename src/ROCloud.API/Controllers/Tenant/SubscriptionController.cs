using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROCloud.API.Filters;
using ROCloud.Application.Common.Models;
using ROCloud.Application.Features.Subscription.Commands.CancelSubscription;
using ROCloud.Application.Features.Subscription.Commands.CompleteUpgrade;
using ROCloud.Application.Features.Subscription.Commands.InitiateSubscription;
using ROCloud.Application.Features.Subscription.Commands.PayInvoice;
using ROCloud.Application.Features.Subscription.Commands.RenewSubscription;
using ROCloud.Application.Features.Subscription.Commands.ResumeSubscription;
using ROCloud.Application.Features.Subscription.Dtos;
using ROCloud.Application.Features.Subscription.Queries.GetSubscription;
using ROCloud.Application.Features.Subscription.Queries.GetSubscriptionInvoiceById;
using ROCloud.Application.Features.Subscription.Queries.GetSubscriptionInvoicePdf;
using ROCloud.Application.Features.Subscription.Queries.GetSubscriptionInvoices;

namespace ROCloud.API.Controllers.Tenant;

/// <summary>
/// The tenant's own ROCloud subscription (guide §25). OWNER ONLY — deliberately not a permission a
/// role can be granted: changing the plan spends the owner's money, so no custom role, however
/// generous, may do it. (The Activity log is restricted the same way.) See CompleteUpgradeCommand for
/// the production webhook security note.
/// </summary>
[ApiController]
[Route("api/subscription")]
[Authorize]
[RequireOwner]
public class SubscriptionController : ControllerBase
{
    private readonly IMediator _mediator;

    public SubscriptionController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
        => Ok(ApiResponse<SubscriptionDto>.Ok(await _mediator.Send(new GetSubscriptionQuery(), ct)));

    [HttpPost("initiate")]
    public async Task<IActionResult> Initiate([FromBody] InitiateSubscriptionRequest body, CancellationToken ct)
        => Ok(ApiResponse<SubscriptionInitiateDto>.Ok(
            await _mediator.Send(new InitiateSubscriptionCommand(body.PlanType, body.BillingCycle ?? "Monthly"), ct)));

    [HttpPost("upgrade-complete")]
    public async Task<IActionResult> Complete([FromBody] InitiateSubscriptionRequest body, CancellationToken ct)
    {
        await _mediator.Send(new CompleteUpgradeCommand(body.PlanType, body.BillingCycle ?? "Monthly", body.OrderId), ct);
        return Ok(ApiResponse<object>.Ok(new { upgraded = true }));
    }

    [HttpGet("invoices")]
    public async Task<IActionResult> Invoices(CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<SubscriptionInvoiceDto>>.Ok(
            await _mediator.Send(new GetSubscriptionInvoicesQuery(), ct)));

    /// <summary>On-demand renewal — raise (or return the open) Pending invoice so the owner can pay
    /// even if the daily expiry job hasn't run. Returns the invoice to pay.</summary>
    [HttpPost("renew")]
    public async Task<IActionResult> Renew(CancellationToken ct)
        => Ok(ApiResponse<SubscriptionInvoiceDto>.Ok(await _mediator.Send(new RenewSubscriptionCommand(), ct)));

    [HttpGet("invoices/{id:guid}")]
    public async Task<IActionResult> Invoice(Guid id, CancellationToken ct)
        => Ok(ApiResponse<SubscriptionInvoiceDto>.Ok(await _mediator.Send(new GetSubscriptionInvoiceByIdQuery(id), ct)));

    [HttpGet("invoices/{id:guid}/pdf")]
    public async Task<IActionResult> InvoicePdf(Guid id, CancellationToken ct)
    {
        var pdf = await _mediator.Send(new GetSubscriptionInvoicePdfQuery(id), ct);
        return File(pdf.Bytes, "application/pdf", pdf.FileName);
    }

    [HttpPost("invoices/{id:guid}/pay-initiate")]
    public async Task<IActionResult> PayInvoiceInitiate(Guid id, CancellationToken ct)
        => Ok(ApiResponse<SubscriptionInitiateDto>.Ok(
            await _mediator.Send(new PayInvoiceInitiateCommand(id), ct)));

    [HttpPost("invoices/{id:guid}/pay-complete")]
    public async Task<IActionResult> PayInvoiceComplete(Guid id, [FromBody] PayInvoiceRequest body, CancellationToken ct)
    {
        await _mediator.Send(new PayInvoiceCompleteCommand(id, body.OrderId), ct);
        return Ok(ApiResponse<object>.Ok(new { paid = true }));
    }

    [HttpPost("cancel")]
    public async Task<IActionResult> Cancel(CancellationToken ct)
    {
        await _mediator.Send(new CancelSubscriptionCommand(), ct);
        return Ok(ApiResponse<object>.Ok(new { cancelled = true }));
    }

    /// <summary>Undo a pending cancellation while still within the paid period.</summary>
    [HttpPost("resume")]
    public async Task<IActionResult> Resume(CancellationToken ct)
    {
        var ok = await _mediator.Send(new ResumeSubscriptionCommand(), ct);
        return ok
            ? Ok(ApiResponse<object>.Ok(new { resumed = true }))
            : BadRequest(ApiResponse<object>.Fail("Nothing to resume — please subscribe.", "NOT_RESUMABLE"));
    }
}

public sealed record InitiateSubscriptionRequest(string PlanType, string? BillingCycle, string? OrderId = null);

public sealed record PayInvoiceRequest(string? OrderId = null);
