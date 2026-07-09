using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ROCloud.API.Filters;

/// <summary>
/// Restricts an endpoint to the tenant's Owner role only — stricter than a permission grant, which a
/// custom role could be given. Reads the `role_name` JWT claim. Combine with [Authorize] so
/// unauthenticated requests get 401 before this runs.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class RequireOwnerAttribute : Attribute, IAsyncActionFilter
{
    private const string OwnerRole = "Owner";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var roleName = context.HttpContext.User.FindFirst("role_name")?.Value;

        if (!string.Equals(roleName, OwnerRole, StringComparison.Ordinal))
        {
            context.Result = new ObjectResult(new
            {
                error = "Forbidden",
                code = "OWNER_ONLY",
                detail = "Only the account owner can access this resource."
            })
            { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }

        await next();
    }
}
