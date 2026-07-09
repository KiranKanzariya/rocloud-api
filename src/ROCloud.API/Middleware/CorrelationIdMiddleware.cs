using Serilog.Context;

namespace ROCloud.API.Middleware;

/// <summary>
/// Assigns a correlation id to every request (guide §16): reads the inbound X-Request-Id or
/// generates one, pushes it onto the Serilog LogContext so every log line carries it, and
/// echoes it back in the response header for client-side debugging.
/// </summary>
public class CorrelationIdMiddleware
{
    private const string HeaderName = "X-Request-Id";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var incoming)
                            && !string.IsNullOrWhiteSpace(incoming)
            ? incoming.ToString()
            : Guid.NewGuid().ToString();

        context.TraceIdentifier = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("RequestId", correlationId))
        {
            await _next(context);
        }
    }
}
