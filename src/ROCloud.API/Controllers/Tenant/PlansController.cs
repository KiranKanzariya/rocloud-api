using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROCloud.Application.Common.Models;
using ROCloud.Application.Features.Plans.Dtos;
using ROCloud.Application.Features.Plans.Queries.GetPlans;

namespace ROCloud.API.Controllers.Tenant;

/// <summary>Subscription plans catalogue (guide §25). Any authenticated user may read it.</summary>
[ApiController]
[Route("api/plans")]
[Authorize]
public class PlansController : ControllerBase
{
    private readonly IMediator _mediator;

    public PlansController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> GetPlans(CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<PlanDto>>.Ok(await _mediator.Send(new GetPlansQuery(), ct)));
}
