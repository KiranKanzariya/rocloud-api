using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROCloud.API.Filters;
using ROCloud.Application.Common.Models;
using ROCloud.Application.Features.Invoices.Commands.BulkGenerateInvoices;
using ROCloud.Application.Features.Invoices.Commands.GenerateInvoice;
using ROCloud.Application.Features.Invoices.Commands.SendInvoice;
using ROCloud.Application.Features.Invoices.Dtos;
using ROCloud.Application.Features.Invoices.Queries.GetInvoiceById;
using ROCloud.Application.Features.Invoices.Queries.GetInvoicePdf;
using ROCloud.Application.Features.Invoices.Queries.GetInvoices;

namespace ROCloud.API.Controllers.Tenant;

[ApiController]
[Route("api/invoices")]
[Authorize]
public class InvoicesController : ControllerBase
{
    private readonly IMediator _mediator;

    public InvoicesController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [RequirePermission("Invoices.View")]
    public async Task<IActionResult> GetInvoices([FromQuery] InvoiceFilterDto filter, CancellationToken ct)
        => Ok(ApiResponse<PagedResult<InvoiceListItemDto>>.Ok(await _mediator.Send(new GetInvoicesQuery(filter), ct)));

    [HttpGet("{id:guid}")]
    [RequirePermission("Invoices.View")]
    public async Task<IActionResult> GetInvoice(Guid id, CancellationToken ct)
        => Ok(ApiResponse<InvoiceDto>.Ok(await _mediator.Send(new GetInvoiceByIdQuery(id), ct)));

    [HttpGet("{id:guid}/pdf")]
    [RequirePermission("Invoices.View")]
    public async Task<IActionResult> GetInvoicePdf(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetInvoicePdfQuery(id), ct);
        return File(result.Content, "application/pdf", result.FileName);
    }

    [HttpPost("generate")]
    [RequirePermission("Invoices.Create")]
    public async Task<IActionResult> Generate([FromBody] GenerateInvoiceRequest body, CancellationToken ct)
    {
        // GstRate left null when the client doesn't specify one, so the handler applies the tenant's
        // GST setting (rate when enabled, 0 when disabled). An explicit body.GstRate still overrides.
        var id = await _mediator.Send(new GenerateInvoiceCommand(
            body.CustomerId, body.PeriodFrom, body.PeriodTo, body.GstRate,
            body.Discount, body.DueInDays, body.Notes), ct);
        return CreatedAtAction(nameof(GetInvoice), new { id }, ApiResponse<object>.Ok(new { id }));
    }

    [HttpPost("bulk-generate")]
    [RequirePermission("Invoices.Create")]
    public async Task<IActionResult> BulkGenerate([FromBody] BulkGenerateInvoicesRequest body, CancellationToken ct)
        => Ok(ApiResponse<BulkInvoiceResultDto>.Ok(await _mediator.Send(
            new BulkGenerateInvoicesCommand(body.PeriodFrom, body.PeriodTo, body.GstRate, body.DueInDays), ct)));

    [HttpPost("{id:guid}/send")]
    [RequirePermission("Invoices.Edit")]
    public async Task<IActionResult> Send(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new SendInvoiceCommand(id), ct);
        return Ok(ApiResponse<object>.Ok(new { id, pdfUrl = result.PdfPath, emailed = result.Emailed }));
    }
}

public sealed record GenerateInvoiceRequest(
    Guid CustomerId, DateOnly PeriodFrom, DateOnly PeriodTo,
    decimal? GstRate, decimal? Discount, int? DueInDays, string? Notes);

public sealed record BulkGenerateInvoicesRequest(
    DateOnly PeriodFrom, DateOnly PeriodTo, decimal? GstRate, int? DueInDays);
