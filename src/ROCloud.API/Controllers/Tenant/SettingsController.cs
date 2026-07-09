using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROCloud.API.Filters;
using ROCloud.Application.Common.Models;
using ROCloud.Application.Features.TenantSettings.Commands.UpdateTenantSettings;
using ROCloud.Application.Features.TenantSettings.Dtos;
using ROCloud.Application.Features.TenantSettings.Queries.GetTenantSettings;

namespace ROCloud.API.Controllers.Tenant;

/// <summary>
/// Tenant business profile / settings (guide §24). Backed by the tenants row. Reads need
/// Settings.View; writes need Settings.Manage.
/// </summary>
[ApiController]
[Route("api/settings")]
[Authorize]
public class SettingsController : ControllerBase
{
    private readonly IMediator _mediator;

    public SettingsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [RequirePermission("Settings.View")]
    public async Task<IActionResult> Get(CancellationToken ct)
        => Ok(ApiResponse<TenantSettingsDto>.Ok(await _mediator.Send(new GetTenantSettingsQuery(), ct)));

    [HttpPut]
    [RequirePermission("Settings.Manage")]
    public async Task<IActionResult> Update([FromBody] UpdateTenantSettingsCommand command, CancellationToken ct)
    {
        await _mediator.Send(command, ct);
        return Ok(ApiResponse<object>.Ok(new { updated = true }));
    }
}
