namespace ROCloud.API.Middleware;

/// <summary>
/// Requires the custom <c>X-Requested-With</c> header on mutating requests (guide §10.6).
/// Browsers cannot set this header cross-origin without triggering CORS, so it blocks
/// classic CSRF. Server-to-server callbacks (e.g. payment webhooks) are exempted.
/// </summary>
public class AntiCsrfMiddleware
{
    // Paths that legitimately POST without the header (verified another way, e.g. signatures).
    private static readonly string[] ExemptPrefixes =
    {
        "/api/payments/razorpay/webhook"
    };

    private readonly RequestDelegate _next;

    public AntiCsrfMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var method = context.Request.Method;
        var isMutating = HttpMethods.IsPost(method) || HttpMethods.IsPut(method)
                         || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

        if (isMutating && !IsExempt(context.Request.Path) &&
            !context.Request.Headers.ContainsKey("X-Requested-With"))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "Missing X-Requested-With header", code = "CSRF_HEADER_MISSING" });
            return;
        }

        await _next(context);
    }

    private static bool IsExempt(PathString path) =>
        ExemptPrefixes.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));
}
