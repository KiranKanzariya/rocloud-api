using Microsoft.AspNetCore.Http;
using ROCloud.API.Middleware;

namespace ROCloud.API.Tests.Middleware;

public class CorrelationIdMiddlewareTests
{
    [Fact]
    public async Task UsesIncomingRequestId_WhenProvided()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Request-Id"] = "abc-123";

        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context);

        Assert.Equal("abc-123", context.TraceIdentifier);
    }

    [Fact]
    public async Task GeneratesGuid_WhenNoRequestIdProvided()
    {
        var context = new DefaultHttpContext();

        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context);

        Assert.True(Guid.TryParse(context.TraceIdentifier, out _),
            $"Expected a GUID correlation id, got '{context.TraceIdentifier}'.");
    }
}
