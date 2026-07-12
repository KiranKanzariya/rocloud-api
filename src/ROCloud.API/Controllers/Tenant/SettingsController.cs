using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROCloud.API.Filters;
using ROCloud.Application.Common.Models;
using ROCloud.Application.Features.TenantSettings.Commands.UpdateTenantSettings;
using ROCloud.Application.Features.TenantSettings.Dtos;
using ROCloud.Application.Features.TenantSettings.Queries.GetBillingSettings;
using ROCloud.Application.Features.TenantSettings.Queries.GetTenantSettings;

namespace ROCloud.API.Controllers.Tenant;

/// <summary>
/// Tenant business profile / settings (guide §24). Backed by the tenants row. Reads need
/// BusinessProfile.View; writes need BusinessProfile.Manage — scoped to THIS page, so a role that may
/// edit the business profile is not thereby able to edit areas, templates or the ROCloud plan.
/// </summary>
[ApiController]
[Route("api/settings")]
[Authorize]
public class SettingsController : ControllerBase
{
    private readonly IMediator _mediator;

    public SettingsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [RequirePermission("BusinessProfile.View")]
    public async Task<IActionResult> Get(CancellationToken ct)
        => Ok(ApiResponse<TenantSettingsDto>.Ok(await _mediator.Send(new GetTenantSettingsQuery(), ct)));

    // The GST rate and number are printed on every invoice, so a role that may read invoices may read
    // them — without also receiving the owner's contact details, plan and billing status from Get().
    [HttpGet("billing")]
    [RequireAnyPermission("BusinessProfile.View", "Invoices.View")]
    public async Task<IActionResult> GetBilling(CancellationToken ct)
        => Ok(ApiResponse<BillingSettingsDto>.Ok(await _mediator.Send(new GetBillingSettingsQuery(), ct)));

    [HttpPut]
    [RequirePermission("BusinessProfile.Manage")]
    public async Task<IActionResult> Update([FromBody] UpdateTenantSettingsCommand command, CancellationToken ct)
    {
        await _mediator.Send(command, ct);
        return Ok(ApiResponse<object>.Ok(new { updated = true }));
    }
}
