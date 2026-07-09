using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ROCloud.API.Filters;

/// <summary>
/// Requires the current user's JWT to carry a specific permission code (guide §4).
/// Combine with [Authorize] so unauthenticated requests get 401 before this runs.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class RequirePermissionAttribute : Attribute, IAsyncActionFilter
{
    private readonly string _permission;

    public RequirePermissionAttribute(string permission) => _permission = permission;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var permissions = context.HttpContext.User.FindFirst("permissions")?.Value?.Split(',')
                          ?? [];

        if (!permissions.Contains(_permission))
        {
            context.Result = new ObjectResult(new
            {
                error = "Forbidden",
                code = "PERMISSION_DENIED",
                detail = $"Required: {_permission}"
            })
            { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }

        await next();
    }
}
