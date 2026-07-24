using System.Text.Json;
using ROCloud.Application.Common.Exceptions;

namespace ROCloud.API.Middleware;

/// <summary>
/// Catches all unhandled exceptions, logs them, and returns a standardised JSON error.
/// Never leaks stack traces outside Development (guide §10.16).
/// </summary>
public class ExceptionMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;
    private readonly IHostEnvironment _env;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger, IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleAsync(context, ex);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception ex)
    {
        var (status, code) = ex switch
        {
            ValidationException => (StatusCodes.Status400BadRequest, "VALIDATION_ERROR"),
            NotFoundException => (StatusCodes.Status404NotFound, "NOT_FOUND"),
            InvalidCredentialsException => (StatusCodes.Status401Unauthorized, "INVALID_CREDENTIALS"),
            AccountLockedException => (StatusCodes.Status429TooManyRequests, "ACCOUNT_LOCKED"),
            TenantBlockedException => (StatusCodes.Status403Forbidden, "TENANT_BLOCKED"),
            ForbiddenAccessException => (StatusCodes.Status403Forbidden, "FORBIDDEN"),
            PlanLimitException => (StatusCodes.Status403Forbidden, "PLAN_LIMIT_REACHED"),
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "FORBIDDEN"),
            _ => (StatusCodes.Status500InternalServerError, "INTERNAL_ERROR")
        };

        if (status >= 500)
            _logger.LogError(ex, "Unhandled exception");
        else
            _logger.LogWarning("Handled {Code}: {Message}", code, ex.Message);

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = status;

        object payload = ex switch
        {
            ValidationException ve => new { error = ve.Message, code, errors = ve.Errors },
            _ => new
            {
                error = status >= 500 && !_env.IsDevelopment() ? "An unexpected error occurred" : ex.Message,
                code,
                detail = _env.IsDevelopment() && status >= 500 ? ex.StackTrace : null
            }
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
    }
}
