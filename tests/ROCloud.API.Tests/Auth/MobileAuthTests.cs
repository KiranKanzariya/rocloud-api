using System.Reflection;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using ROCloud.API.Controllers.Tenant;
using ROCloud.Application.Features.Auth.Common;

namespace ROCloud.API.Tests.Auth;

/// <summary>
/// The mobile client has no cookie jar, so it needs the refresh token in the body. These tests
/// exist mainly to prove the WEB path did not change while adding it — the HttpOnly cookie is what
/// stops XSS on the portal from stealing a refresh token, and losing it silently would be severe.
/// </summary>
public class MobileAuthTests
{
    private const string TestRefreshToken = "user-id.random-secret";

    /// <summary>Answers every command with a fixed <see cref="AuthResult"/>. No Moq in this repo.</summary>
    private sealed class StubMediator : IMediator
    {
        public object? LastRequest { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
        {
            LastRequest = request;
            object result = new AuthResult("access-token", DateTime.UtcNow.AddHours(1), TestRefreshToken);
            return Task.FromResult((TResponse)result);
        }

        public Task<object?> Send(object request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken ct = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task Publish(object notification, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task Publish<TNotification>(TNotification notification, CancellationToken ct = default)
            where TNotification : INotification => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private static (AuthController Controller, DefaultHttpContext Context) Build(bool native)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:RefreshTokenExpiryDays"] = "30"
            })
            .Build();

        var context = new DefaultHttpContext();
        if (native) context.Request.Headers["X-Client"] = "mobile";

        var controller = new AuthController(new StubMediator(), config)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };

        return (controller, context);
    }

    private static Dictionary<string, object?> BodyOf(IActionResult result)
    {
        var value = Assert.IsType<OkObjectResult>(result).Value!;
        return value.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(p => p.Name, p => p.GetValue(value));
    }

    private static string? SetCookieHeader(HttpContext context) =>
        context.Response.Headers.SetCookie.FirstOrDefault();

    // ─── web: unchanged ───────────────────────────────────────────────────

    [Fact]
    public async Task WebLogin_StillSetsHttpOnlyCookie_AndKeepsTokenOutOfTheBody()
    {
        var (controller, context) = Build(native: false);

        var result = await controller.Login(new LoginRequest("owner@aquapure.in", "pw"), default);

        var body = BodyOf(result);
        Assert.True(body.ContainsKey("accessToken"));
        Assert.False(body.ContainsKey("refreshToken")); // the whole point of the cookie

        var cookie = SetCookieHeader(context);
        Assert.NotNull(cookie);
        Assert.Contains("refresh_token=", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/auth/refresh", cookie, StringComparison.OrdinalIgnoreCase);
    }

    // ─── mobile: token in the body, no cookie ─────────────────────────────

    [Fact]
    public async Task MobileLogin_ReturnsRefreshTokenInBody_AndSetsNoCookie()
    {
        var (controller, context) = Build(native: true);

        var result = await controller.Login(new LoginRequest("owner@aquapure.in", "pw"), default);

        var body = BodyOf(result);
        Assert.Equal(TestRefreshToken, body["refreshToken"]);
        Assert.True(body.ContainsKey("accessToken"));
        Assert.Null(SetCookieHeader(context));
    }

    // ─── refresh accepts either carrier ───────────────────────────────────

    [Fact]
    public async Task Refresh_AcceptsTheTokenFromTheBody_WhenThereIsNoCookie()
    {
        var (controller, _) = Build(native: true);

        var result = await controller.Refresh(new RefreshRequest("stored-refresh-token"), default);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Refresh_PrefersTheCookieOverTheBody()
    {
        var (controller, context) = Build(native: false);
        context.Request.Headers.Cookie = "refresh_token=cookie-token";

        var result = await controller.Refresh(new RefreshRequest("body-token"), default);

        Assert.IsType<OkObjectResult>(result);
        // A browser's own HttpOnly cookie must not be overridable by request content.
        var sent = (StubMediator)GetMediator(controller);
        Assert.Contains("cookie-token", sent.LastRequest!.ToString());
    }

    [Fact]
    public async Task Refresh_WithNeitherCookieNorBody_Is401()
    {
        var (controller, _) = Build(native: true);

        var result = await controller.Refresh(null, default);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Contains("NO_REFRESH_TOKEN", unauthorized.Value!.ToString());
    }

    private static IMediator GetMediator(AuthController controller) =>
        (IMediator)typeof(AuthController)
            .GetField("_mediator", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(controller)!;
}
