using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ROCloud.API.Filters;

/// <summary>
/// Requires the tenant's plan to be at least the given tier (Basic &lt; Pro &lt; Enterprise).
/// Returns 403 with an upgrade hint otherwise.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class RequirePlanAttribute : Attribute, IAsyncActionFilter
{
    private static readonly Dictionary<string, int> PlanOrder = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Basic"] = 1,
        ["Pro"] = 2,
        ["Enterprise"] = 3
    };

    private readonly string _minimumPlan;

    public RequirePlanAttribute(string minimumPlan) => _minimumPlan = minimumPlan;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var current = context.HttpContext.User.FindFirst("plan_type")?.Value ?? "Basic";
        var currentRank = PlanOrder.GetValueOrDefault(current, 0);
        var requiredRank = PlanOrder.GetValueOrDefault(_minimumPlan, int.MaxValue);

        if (currentRank < requiredRank)
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
