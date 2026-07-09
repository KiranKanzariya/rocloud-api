using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROCloud.API.Filters;
using ROCloud.Application.Common.Models;
using ROCloud.Application.Features.Platform.Dashboard.Dtos;
using ROCloud.Application.Features.Platform.Dashboard.Queries.GetPlatformDashboard;

namespace ROCloud.API.Controllers.Platform;

/// <summary>Platform KPI dashboard (guide §26). Any authenticated platform staff member.</summary>
[ApiController]
[Route("api/platform/dashboard")]
[Authorize]
[RequirePlatformRole]
public class PlatformDashboardController : ControllerBase
{
    private readonly IMediator _mediator;

    public PlatformDashboardController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
        => Ok(ApiResponse<PlatformDashboardDto>.Ok(await _mediator.Send(new GetPlatformDashboardQuery(), ct)));
}
