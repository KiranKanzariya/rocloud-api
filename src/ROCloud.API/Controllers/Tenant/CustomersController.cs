using ROCloud.Application.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROCloud.API.Filters;
using ROCloud.Application.Common.Models;
using ROCloud.Application.Features.Customers.Commands.CreateCustomer;
using ROCloud.Application.Features.Customers.Commands.DeleteCustomer;
using ROCloud.Application.Features.Customers.Commands.ClearCustomerOpeningBalance;
using ROCloud.Application.Features.Customers.Commands.ImportCustomers;
using ROCloud.Application.Features.Customers.Commands.SetCustomerDiscount;
using ROCloud.Application.Features.Customers.Commands.SetCustomerOpeningBalance;
using ROCloud.Application.Features.Customers.Commands.UpdateCustomer;
using ROCloud.Application.Features.Customers.Dtos;
using ROCloud.Application.Features.CustomerSubscriptions.Commands.CancelCustomerSubscription;
using ROCloud.Application.Features.CustomerSubscriptions.Commands.CreateCustomerSubscription;
using ROCloud.Application.Features.CustomerSubscriptions.Commands.UpdateCustomerSubscription;
using ROCloud.Application.Features.Customers.Queries.GetCustomerById;
using ROCloud.Application.Features.Customers.Queries.GetCustomerJarBalance;
using ROCloud.Application.Features.Customers.Queries.GetCustomerOpeningBalance;
using ROCloud.Application.Features.Customers.Queries.GetCustomers;
using ROCloud.Application.Features.Customers.Queries.GetCustomerStats;

namespace ROCloud.API.Controllers.Tenant;

[ApiController]
[Route("api/customers")]
[Authorize]
public class CustomersController : ControllerBase
{
    private readonly IMediator _mediator;

    public CustomersController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [RequirePermission("Customers.View")]
    public async Task<IActionResult> GetCustomers([FromQuery] CustomerFilterDto filter, CancellationToken ct)
        => Ok(ApiResponse<PagedResult<CustomerListItemDto>>.Ok(await _mediator.Send(new GetCustomersQuery(filter), ct)));

    [HttpGet("{id:guid}")]
    [RequirePermission("Customers.View")]
    public async Task<IActionResult> GetCustomer(Guid id, CancellationToken ct)
        => Ok(ApiResponse<CustomerDto>.Ok(await _mediator.Send(new GetCustomerByIdQuery(id), ct)));

    [HttpGet("{id:guid}/stats")]
    [RequirePermission("Customers.View")]
    public async Task<IActionResult> GetCustomerStats(Guid id, CancellationToken ct)
        => Ok(ApiResponse<CustomerStatsDto>.Ok(await _mediator.Send(new GetCustomerStatsQuery(id), ct)));

    /// <summary>Net returnable jars the customer still holds, per product (Σ Issue − Σ Return).</summary>
    [HttpGet("{id:guid}/jar-balance")]
    [RequirePermission("Customers.View")]
    public async Task<IActionResult> GetJarBalance(Guid id, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<CustomerJarBalanceDto>>.Ok(
            await _mediator.Send(new GetCustomerJarBalanceQuery(id), ct)));

    [HttpPost]
    [RequirePermission("Customers.Create")]
    public async Task<IActionResult> CreateCustomer([FromBody] CreateCustomerCommand command, CancellationToken ct)
    {
        var id = await _mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetCustomer), new { id }, ApiResponse<object>.Ok(new { id }));
    }

    [HttpPut("{id:guid}")]
    [RequirePermission("Customers.Edit")]
    public async Task<IActionResult> UpdateCustomer(Guid id, [FromBody] UpdateCustomerRequest body, CancellationToken ct)
    {
        await _mediator.Send(new UpdateCustomerCommand(
            id, body.AreaId, body.Name, body.Mobile, body.AlternateMobile, body.Email,
            body.AddressLine, body.Landmark, body.Latitude, body.Longitude,
            body.DeliveryMode, body.PaymentPreference, body.PreferredBottleSize,
            body.PreferredLanguage, body.Notes, body.IsActive), ct);
        return Ok(ApiResponse<object>.Ok(new { id }));
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission("Customers.Delete")]
    public async Task<IActionResult> DeleteCustomer(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeleteCustomerCommand(id), ct);
        return Ok(ApiResponse<object>.Ok(new { id }));
    }

    /// <summary>Set the customer's standing discount on their water invoices (guide §26).</summary>
    [HttpPut("{id:guid}/discount")]
    [RequirePermission("Customers.Edit")]
    public async Task<IActionResult> SetDiscount(Guid id, [FromBody] SetCustomerDiscountRequest body, CancellationToken ct)
    {
        await _mediator.Send(new SetCustomerDiscountCommand(id, body.DiscountType, body.DiscountValue), ct);
        return Ok(ApiResponse<object>.Ok(new { id, body.DiscountType, body.DiscountValue }));
    }

    /// <summary>Current opening balance (if any) seeded for the customer — powers the migration card.</summary>
    [HttpGet("{id:guid}/opening-balance")]
    [RequirePermission("Customers.View")]
    public async Task<IActionResult> GetOpeningBalance(Guid id, CancellationToken ct)
        => Ok(ApiResponse<CustomerOpeningBalanceDto>.Ok(
            await _mediator.Send(new GetCustomerOpeningBalanceQuery(id), ct)));

    /// <summary>Seed a customer's opening jars-held + dues/advance when migrating from a paper book.</summary>
    [HttpPost("{id:guid}/opening-balance")]
    [RequirePermission("Customers.Edit")]
    public async Task<IActionResult> SetOpeningBalance(Guid id, [FromBody] SetCustomerOpeningBalanceRequest body, CancellationToken ct)
    {
        await _mediator.Send(new SetCustomerOpeningBalanceCommand(
            id, body.CutoverDate, body.Jars ?? [], body.OpeningDues, body.Note), ct);
        return Ok(ApiResponse<object>.Ok(new { id }));
    }

    /// <summary>Undo a seeded opening balance (fix a migration mistake before go-live).</summary>
    [HttpDelete("{id:guid}/opening-balance")]
    [RequirePermission("Customers.Edit")]
    public async Task<IActionResult> ClearOpeningBalance(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new ClearCustomerOpeningBalanceCommand(id), ct);
        return Ok(ApiResponse<object>.Ok(new { id }));
    }

    /// <summary>
    /// Bulk-import customers from the migration CSV. Pass dryRun=true to validate &amp; preview without
    /// writing; dryRun=false to commit. Each row → customer (+ opening balance + subscription).
    /// </summary>
    [HttpPost("import")]
    [RequirePermission("Customers.Create")]
    public async Task<IActionResult> Import(
        IFormFile? file, [FromQuery] bool dryRun, [FromQuery] DateOnly? cutoverDate, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "A CSV file is required." });

        using var reader = new StreamReader(file.OpenReadStream());
        var csv = await reader.ReadToEndAsync(ct);

        var result = await _mediator.Send(new ImportCustomersCommand(
            csv, dryRun, cutoverDate ?? AppTimeZone.Today(DateTime.UtcNow)), ct);
        return Ok(ApiResponse<ImportCustomersResultDto>.Ok(result));
    }

    /// <summary>Add a recurring delivery subscription that the nightly job turns into orders (guide §9).</summary>
    [HttpPost("{id:guid}/subscriptions")]
    [RequirePermission("Customers.Edit")]
    public async Task<IActionResult> CreateSubscription(
        Guid id, [FromBody] CreateCustomerSubscriptionRequest body, CancellationToken ct)
    {
        var subId = await _mediator.Send(new CreateCustomerSubscriptionCommand(
            id, body.ProductId, body.Quantity, body.Frequency, body.RatePerUnit, body.StartDate), ct);
        return Ok(ApiResponse<object>.Ok(new { id = subId }));
    }

    /// <summary>Edit a customer's delivery subscription in place (quantity / frequency / rate).</summary>
    [HttpPut("{id:guid}/subscriptions/{subId:guid}")]
    [RequirePermission("Customers.Edit")]
    public async Task<IActionResult> UpdateSubscription(
        Guid id, Guid subId, [FromBody] UpdateCustomerSubscriptionRequest body, CancellationToken ct)
    {
        await _mediator.Send(new UpdateCustomerSubscriptionCommand(
            subId, id, body.Quantity, body.Frequency, body.RatePerUnit), ct);
        return Ok(ApiResponse<object>.Ok(new { id = subId }));
    }

    /// <summary>End a customer's delivery subscription.</summary>
    [HttpPost("{id:guid}/subscriptions/{subId:guid}/cancel")]
    [RequirePermission("Customers.Edit")]
    public async Task<IActionResult> CancelSubscription(Guid id, Guid subId, CancellationToken ct)
    {
        await _mediator.Send(new CancelCustomerSubscriptionCommand(subId), ct);
        return Ok(ApiResponse<object>.Ok(new { id = subId }));
    }
}

public sealed record SetCustomerDiscountRequest(string DiscountType, decimal DiscountValue);

public sealed record CreateCustomerSubscriptionRequest(
    Guid ProductId, int Quantity, string Frequency, decimal? RatePerUnit, DateOnly? StartDate);

public sealed record UpdateCustomerSubscriptionRequest(
    int Quantity, string Frequency, decimal? RatePerUnit);

public sealed record UpdateCustomerRequest(
    Guid? AreaId, string Name, string? Mobile, string? AlternateMobile, string? Email,
    string? AddressLine, string? Landmark, decimal? Latitude, decimal? Longitude,
    string DeliveryMode, string PaymentPreference, string? PreferredBottleSize,
    string? PreferredLanguage, string? Notes, bool IsActive);
