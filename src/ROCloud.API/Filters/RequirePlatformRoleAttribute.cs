using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ROCloud.API.Filters;

/// <summary>
/// Restricts a platform endpoint to the given platform_role(s). SuperAdmin always passes.
/// Returns 403 otherwise. Used by the super-admin portal controllers (guide §26).
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class RequirePlatformRoleAttribute : Attribute, IAsyncActionFilter
{
    private readonly string[] _allowedRoles;

    public RequirePlatformRoleAttribute(params string[] allowedRoles) => _allowedRoles = allowedRoles;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var role = context.HttpContext.User.FindFirst("platform_role")?.Value;

        if (string.IsNullOrEmpty(role))
        {
            context.Result = new ObjectResult(new { error = "Platform access required.", code = "PLATFORM_FORBIDDEN" })
            { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }

        // SuperAdmin has access to everything; otherwise the role must be in the allowed set.
        if (!string.Equals(role, "SuperAdmin", StringComparison.Ordinal)
            && _allowedRoles.Length > 0
            && !_allowedRoles.Contains(role, StringComparer.Ordinal))
        {
            context.Result = new ObjectResult(new
            {
                error = "Insufficient platform role.",
                code = "PLATFORM_ROLE_REQUIRED",
                detail = $"Requires one of: {string.Join(", ", _allowedRoles)}."
            })
            { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }

        await next();
    }
}
