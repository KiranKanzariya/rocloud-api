using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROCloud.API.Filters;
using ROCloud.Application.Common.Models;
using ROCloud.Application.Features.Platform.Plans.Commands.DeletePlan;
using ROCloud.Application.Features.Platform.Plans.Commands.UpsertPlan;
using ROCloud.Application.Features.Platform.Plans.Dtos;
using ROCloud.Application.Features.Platform.Plans.Queries.GetAllPlans;

namespace ROCloud.API.Controllers.Platform;

/// <summary>Platform subscription-plan administration (guide §26). Writes are SuperAdmin only.</summary>
// Plans are a SuperAdmin-only concern (guide §26) — including reads.
[ApiController]
[Route("api/platform/plans")]
[Authorize]
[RequirePlatformRole("SuperAdmin")]
public class PlatformPlansController : ControllerBase
{
    private readonly IMediator _mediator;

    public PlatformPlansController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> GetPlans(CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<PlatformPlanDto>>.Ok(await _mediator.Send(new GetAllPlansQuery(), ct)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] UpsertPlanCommand command, CancellationToken ct)
    {
        var id = await _mediator.Send(command with { Id = null }, ct);
        return Ok(ApiResponse<object>.Ok(new { id }));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpsertPlanCommand command, CancellationToken ct)
    {
        var resultId = await _mediator.Send(command with { Id = id }, ct);
        return Ok(ApiResponse<object>.Ok(new { id = resultId }));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeletePlanCommand(id), ct);
        return Ok(ApiResponse<object>.Ok(new { id }));
    }
}
