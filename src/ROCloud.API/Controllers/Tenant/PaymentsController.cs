using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROCloud.API.Filters;
using ROCloud.Application.Common.Models;
using ROCloud.Application.Features.Payments.Commands.CollectPayment;
using ROCloud.Application.Features.Payments.Commands.ConfirmRazorpayPayment;
using ROCloud.Application.Features.Payments.Commands.InitiateRazorpayPayment;
using ROCloud.Application.Features.Payments.Dtos;
using ROCloud.Application.Features.Payments.Queries.GetCustomerUpiQr;
using ROCloud.Application.Features.Payments.Queries.GetOutstandingDues;
using ROCloud.Application.Features.Payments.Queries.GetPaymentSummary;
using ROCloud.Application.Features.Payments.Queries.GetPayments;

namespace ROCloud.API.Controllers.Tenant;

[ApiController]
[Route("api/payments")]
[Authorize]
public class PaymentsController : ControllerBase
{
    private readonly IMediator _mediator;

    public PaymentsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [RequirePermission("Payments.View")]
    public async Task<IActionResult> GetPayments([FromQuery] PaymentFilterDto filter, CancellationToken ct)
        => Ok(ApiResponse<PagedResult<PaymentListItemDto>>.Ok(await _mediator.Send(new GetPaymentsQuery(filter), ct)));

    /// <summary>Collection totals for a window, summed in the DB — the money tiles must not depend on a page size.</summary>
    [HttpGet("summary")]
    [RequirePermission("Payments.View")]
    public async Task<IActionResult> GetSummary(
        [FromQuery] DateOnly? fromDate, [FromQuery] DateOnly? toDate, CancellationToken ct)
        => Ok(ApiResponse<PaymentSummaryDto>.Ok(
            await _mediator.Send(new GetPaymentSummaryQuery(fromDate, toDate), ct)));

    [HttpGet("outstanding")]
    [RequirePermission("Payments.View")]
    /// <param name="overdueDays">
    /// How aged a debt must be to be listed. Omitted → 7, the dunning default. Pass 0 for EVERYONE who
    /// owes as of today — what the money-in worklist needs, since a customer who just paid by UPI QR
    /// may owe on a delivery made this morning. Nullable on purpose: an omitted query parameter binds
    /// to 0, so the old `<= 0 ? 7` could never tell "not specified" from "no ageing filter, please".
    /// </param>
    public async Task<IActionResult> GetOutstanding([FromQuery] int? overdueDays, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<OutstandingDueDto>>.Ok(
            await _mediator.Send(new GetOutstandingDuesQuery(overdueDays ?? 7), ct)));

    /// <summary>
    /// Scan-to-pay payload for what this customer owes now — the counter QR.
    ///
    /// Payments.View, not .Collect: this is a read, and gating a GET behind a write permission is
    /// exactly what ReadEndpointPermissionTests forbids. It withholds nothing either — the same UPI id
    /// is printed on every invoice PDF and sits in Business profile. The money-in list still shows the
    /// button only to users who can collect, which is a workflow choice, not an access one.
    /// </summary>
    [HttpGet("customers/{customerId:guid}/upi-qr")]
    [RequirePermission("Payments.View")]
    public async Task<IActionResult> GetCustomerUpiQr(Guid customerId, CancellationToken ct)
        => Ok(ApiResponse<CustomerUpiQrDto>.Ok(
            await _mediator.Send(new GetCustomerUpiQrQuery(customerId), ct)));

    [HttpPost]
    [RequirePermission("Payments.Collect")]
    public async Task<IActionResult> Collect([FromBody] CollectPaymentCommand command, CancellationToken ct)
    {
        var id = await _mediator.Send(command, ct);
        return Ok(ApiResponse<object>.Ok(new { id }));
    }

    [HttpPost("razorpay/initiate")]
    [RequirePermission("Payments.Collect")]
    public async Task<IActionResult> InitiateRazorpay(
        [FromBody] InitiateRazorpayPaymentCommand command, CancellationToken ct)
        => Ok(ApiResponse<RazorpayInitiateResultDto>.Ok(await _mediator.Send(command, ct)));

    /// <summary>
    /// Razorpay webhook. Anonymous by route, but the signature is verified in the handler
    /// against the RAW body (guide §10) — the attribute alone is never trusted.
    /// </summary>
    [HttpPost("razorpay/webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> RazorpayWebhook(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var rawBody = await reader.ReadToEndAsync(ct);
        var signature = Request.Headers["X-Razorpay-Signature"].FirstOrDefault();

        if (!TryExtractIds(rawBody, out var orderId, out var paymentId))
            return Ok();   // ignore payloads we don't recognise (still acks Razorpay)

        await _mediator.Send(new ConfirmRazorpayPaymentCommand(rawBody, signature, orderId, paymentId), ct);
        return Ok();
    }

    private static bool TryExtractIds(string rawBody, out string orderId, out string paymentId)
    {
        orderId = string.Empty;
        paymentId = string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var entity = doc.RootElement
                .GetProperty("payload").GetProperty("payment").GetProperty("entity");
            orderId = entity.GetProperty("order_id").GetString() ?? string.Empty;
            paymentId = entity.GetProperty("id").GetString() ?? string.Empty;
            return orderId.Length > 0 && paymentId.Length > 0;
        }
        catch
        {
            return false;
        }
    }
}
