using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROCloud.Application.Common.Models;
using ROCloud.Application.Features.Plans.Dtos;
using ROCloud.Application.Features.Plans.Queries.GetPlans;

namespace ROCloud.API.Controllers.Tenant;

/// <summary>Subscription plans catalogue (guide §25).</summary>
[ApiController]
[Route("api/plans")]
[Authorize]
public class PlansController : ControllerBase
{
    private readonly IMediator _mediator;

    public PlansController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// The plan catalogue is public pricing information: the anonymous sign-up wizard renders its
    /// plan cards from this response, so prices and limits can never drift from the plans table.
    /// </summary>
    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> GetPlans(CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<PlanDto>>.Ok(await _mediator.Send(new GetPlansQuery(), ct)));
}
