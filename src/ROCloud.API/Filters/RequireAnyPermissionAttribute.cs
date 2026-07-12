using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ROCloud.API.Filters;

/// <summary>
/// Requires the current user's JWT to carry at least one of the given permission codes.
/// Used where a single capability is reachable from more than one grant — e.g. reading the role
/// list is legitimate for both <c>Roles.View</c> and the legacy <c>Roles.Manage</c>, which keeps
/// already-issued tokens working while they roll over (permissions are a snapshot taken at login).
/// Combine with [Authorize] so unauthenticated requests get 401 before this runs.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class RequireAnyPermissionAttribute : Attribute, IAsyncActionFilter
{
    public IReadOnlyList<string> Permissions { get; }

    public RequireAnyPermissionAttribute(params string[] permissions) => Permissions = permissions;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var held = context.HttpContext.User.FindFirst("permissions")?.Value?.Split(',')
                   ?? [];

        if (!Permissions.Any(held.Contains))
        {
            context.Result = new ObjectResult(new
            {
                error = "Forbidden",
                code = "PERMISSION_DENIED",
                detail = $"Required any of: {string.Join(", ", Permissions)}"
            })
            { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }

        await next();
    }
}
