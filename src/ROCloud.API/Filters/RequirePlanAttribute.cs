using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Subscription;
using ROCloud.Domain.Enums;

namespace ROCloud.API.Filters;

/// <summary>
/// Requires the tenant's plan to be at least the given tier. Returns 403 with an upgrade hint otherwise.
/// Tier order is the PlanType declaration order (Basic &lt; Pro &lt; Enterprise), which the plans.plan_type
/// CHECK constraint mirrors.
///
/// The tier is read from the database, not from the JWT's plan_type claim: the admin portal can change a
/// tenant's plan without re-issuing that tenant's token, so the claim lags by up to an access-token
/// lifetime — long enough for a downgraded tenant to keep using the feature they no longer pay for.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class RequirePlanAttribute : Attribute, IAsyncActionFilter
{
    private readonly PlanType _minimumPlan;

    public RequirePlanAttribute(PlanType minimumPlan) => _minimumPlan = minimumPlan;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var services = context.HttpContext.RequestServices;
        var db = services.GetRequiredService<IAppDbContext>();
        var tenantContext = services.GetRequiredService<ITenantContext>();

        var tier = await PlanFeatures.TierAsync(db, tenantContext.TenantId, context.HttpContext.RequestAborted);

        if (tier is null || tier < _minimumPlan)
        {
            context.Result = new ObjectResult(new
            {
                error = "Upgrade required",
                code = "PLAN_UPGRADE_REQUIRED",
                detail = $"This feature requires the {_minimumPlan} plan."
            })
            { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }

        await next();
    }
}
