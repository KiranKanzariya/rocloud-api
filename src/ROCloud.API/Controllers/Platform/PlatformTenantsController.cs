using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROCloud.API.Filters;
using ROCloud.Application.Common.Models;
using ROCloud.Application.Features.Platform.Tenants.Commands.ChangeTenantPlan;
using ROCloud.Application.Features.Platform.Tenants.Commands.ChangeTenantSubdomain;
using ROCloud.Application.Features.Platform.Tenants.Commands.GrantFreeMonths;
using ROCloud.Application.Features.Platform.Tenants.Commands.ImpersonateTenant;
using ROCloud.Application.Features.Platform.Tenants.Commands.SetTenantStatus;
using ROCloud.Application.Features.Platform.Tenants.Commands.SetTenantSubscriptionDiscount;
using ROCloud.Application.Features.Platform.Tenants.Dtos;
using ROCloud.Application.Features.Platform.Tenants.Queries.GetTenantById;
using ROCloud.Application.Features.Platform.Tenants.Queries.GetTenants;

namespace ROCloud.API.Controllers.Platform;

/// <summary>Platform tenant management (guide §26).</summary>
[ApiController]
[Route("api/platform/tenants")]
[Authorize]
[RequirePlatformRole]
public class PlatformTenantsController : ControllerBase
{
    private readonly IMediator _mediator;

    public PlatformTenantsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> GetTenants([FromQuery] TenantFilterDto filter, CancellationToken ct)
        => Ok(ApiResponse<PagedResult<TenantListItemDto>>.Ok(await _mediator.Send(new GetTenantsQuery(filter), ct)));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetTenant(Guid id, CancellationToken ct)
        => Ok(ApiResponse<TenantDetailDto>.Ok(await _mediator.Send(new GetTenantByIdQuery(id), ct)));

    [HttpPost("{id:guid}/suspend")]
    [RequirePlatformRole("Support")]
    public async Task<IActionResult> Suspend(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new SetTenantStatusCommand(id, "Suspended"), ct);
        return Ok(ApiResponse<object>.Ok(new { id, status = "Suspended" }));
    }

    [HttpPost("{id:guid}/reactivate")]
    [RequirePlatformRole("Support")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new SetTenantStatusCommand(id, "Active"), ct);
        return Ok(ApiResponse<object>.Ok(new { id, status = "Active" }));
    }

    [HttpPost("{id:guid}/change-plan")]
    [RequirePlatformRole("SuperAdmin")]
    public async Task<IActionResult> ChangePlan(Guid id, [FromBody] ChangePlanRequest body, CancellationToken ct)
    {
        await _mediator.Send(new ChangeTenantPlanCommand(id, body.PlanType), ct);
        return Ok(ApiResponse<object>.Ok(new { id, planType = body.PlanType }));
    }

    /// <summary>Rename the tenant's subdomain (SuperAdmin only) — the only way to fix a wrong one.</summary>
    [HttpPut("{id:guid}/subdomain")]
    [RequirePlatformRole("SuperAdmin")]
    public async Task<IActionResult> ChangeSubdomain(Guid id, [FromBody] ChangeSubdomainRequest body, CancellationToken ct)
    {
        var subdomain = await _mediator.Send(new ChangeTenantSubdomainCommand(id, body.Subdomain), ct);
        return Ok(ApiResponse<object>.Ok(new { id, subdomain }));
    }

    /// <summary>Set the tenant's standing discount on their ROCloud subscription price (SuperAdmin only).</summary>
    [HttpPut("{id:guid}/subscription-discount")]
    [RequirePlatformRole("SuperAdmin")]
    public async Task<IActionResult> SetSubscriptionDiscount(
        Guid id, [FromBody] SetSubscriptionDiscountRequest body, CancellationToken ct)
    {
        await _mediator.Send(new SetTenantSubscriptionDiscountCommand(id, body.DiscountType, body.DiscountValue), ct);
        return Ok(ApiResponse<object>.Ok(new { id, body.DiscountType, body.DiscountValue }));
    }

    /// <summary>Grant the tenant N free months by extending their subscription end date (SuperAdmin only).</summary>
    [HttpPost("{id:guid}/grant-free-months")]
    [RequirePlatformRole("SuperAdmin")]
    public async Task<IActionResult> GrantFreeMonths(
        Guid id, [FromBody] GrantFreeMonthsRequest body, CancellationToken ct)
    {
        var endsAt = await _mediator.Send(new GrantFreeMonthsCommand(id, body.Months), ct);
        return Ok(ApiResponse<object>.Ok(new { id, body.Months, subscriptionEndsAt = endsAt }));
    }

    /// <summary>Impersonate a tenant's Owner — returns a tenant access token (SuperAdmin only, audited).</summary>
    [HttpPost("/api/platform/impersonate/{id:guid}")]
    [RequirePlatformRole("SuperAdmin")]
    public async Task<IActionResult> Impersonate(Guid id, CancellationToken ct)
        => Ok(ApiResponse<ImpersonateResultDto>.Ok(await _mediator.Send(new ImpersonateTenantCommand(id), ct)));
}

public sealed record ChangePlanRequest(string PlanType);
public sealed record ChangeSubdomainRequest(string Subdomain);
public sealed record SetSubscriptionDiscountRequest(string DiscountType, decimal DiscountValue);
public sealed record GrantFreeMonthsRequest(int Months);
