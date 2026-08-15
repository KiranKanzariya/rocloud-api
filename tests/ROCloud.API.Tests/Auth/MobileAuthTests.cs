using System.Reflection;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using ROCloud.API.Controllers.Tenant;
using ROCloud.Application.Features.Auth.Commands.Logout;
using ROCloud.Application.Features.Auth.Common;
using RefreshCmd = ROCloud.Application.Features.Auth.Commands.RefreshToken.RefreshTokenCommand;

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

        /// <summary>The void-returning overload — how LogoutCommand is dispatched.</summary>
        public Task Send<TRequest>(TRequest request, CancellationToken ct = default)
            where TRequest : IRequest
        {
            LastRequest = request;
            return Task.CompletedTask;
        }

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
        var (controller, context, _) = BuildWithMediator(native);
        return (controller, context);
    }

    /// <summary>As [Build], but hands back the stub so a test can read the command it was sent.</summary>
    private static (AuthController Controller, DefaultHttpContext Context, StubMediator Mediator)
        BuildWithMediator(bool native = false)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:RefreshTokenExpiryDays"] = "30"
            })
            .Build();

        var context = new DefaultHttpContext();
        if (native) context.Request.Headers["X-Client"] = "mobile";

        var mediator = new StubMediator();
        var controller = new AuthController(mediator, config)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };

        return (controller, context, mediator);
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

    private static string AllSetCookies(HttpContext context) =>
        context.Response.Headers.SetCookie.ToString();

    // ─── web: the cookie, and only the cookie ─────────────────────────────

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
        Assert.Contains("roc_rt=", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        // Widened from /api/auth/refresh so the cookie also reaches /api/auth/logout — without that,
        // a web sign-out could not revoke its own session row.
        Assert.Contains("path=/api/auth", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WebLogin_NamesTheCookieAfterTheWorkspace()
    {
        // A cookie is keyed by host + path + NAME, and this one is set by the API host rather
        // than by a tenant's subdomain. Under a single name, signing in to a second workspace
        // overwrote the first: two tabs, and whichever was signed into last owned the only
        // session the browser could carry — reloading the other tab signed it out.
        var (controller, context) = Build(native: false);
        context.Request.Headers["X-Tenant"] = "aqua";

        await controller.Login(new LoginRequest("owner@aqua.in", "pw"), default);

        Assert.Contains("roc_rt_aqua=", AllSetCookies(context));
    }

    [Fact]
    public async Task WebLogin_OnAnotherWorkspace_LeavesTheFirstCookieAlone()
    {
        var (controller, context) = Build(native: false);
        context.Request.Headers["X-Tenant"] = "pani";
        // The browser is already carrying Aqua's session from another tab.
        context.Request.Headers.Cookie = "roc_rt_aqua=aqua-token";

        await controller.Login(new LoginRequest("owner@pani.in", "pw"), default);

        var cookies = AllSetCookies(context);
        Assert.Contains("roc_rt_pani=", cookies);
        // Nothing was written for Aqua, so its tab keeps the session it had.
        Assert.DoesNotContain("roc_rt_aqua=", cookies);
    }

    [Fact]
    public async Task Refresh_ReadsThisWorkspacesCookie_NotAnotherTabs()
    {
        var (controller, context, mediator) = BuildWithMediator();
        context.Request.Headers["X-Tenant"] = "aqua";
        context.Request.Headers.Cookie = "roc_rt_pani=pani-token; roc_rt_aqua=aqua-token";

        var result = await controller.Refresh(null, default);

        Assert.IsType<OkObjectResult>(result);
        var sent = Assert.IsType<RefreshCmd>(mediator.LastRequest);
        Assert.Equal("aqua-token", sent.RefreshToken);
        Assert.Equal("aqua", sent.Subdomain);
    }

    // ─── the X-Client header is not a client ──────────────────────────────

    [Fact]
    public async Task Refresh_WithACookie_IgnoresTheMobileHeader_AndAnswersWithACookie()
    {
        // The carrier follows the CREDENTIAL, not a header the caller picks. Script on the portal
        // could otherwise add X-Client: mobile to a credentialed fetch against /api/auth/refresh and
        // read the rotated refresh token straight out of the JSON — turning any XSS from "acts as the
        // user while the tab is open" into "holds a 30-day credential". A real native client has no
        // cookie jar, so it never trips this.
        var (controller, context) = Build(native: true);
        context.Request.Headers.Cookie = "roc_rt=cookie-token";

        var result = await controller.Refresh(null, default);

        Assert.False(BodyOf(result).ContainsKey("refreshToken"));
        Assert.Contains("roc_rt=", AllSetCookies(context));
    }

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

    // ─── sign-out has to work when the access token has expired ───────────

    [Fact]
    public void Logout_IsAnonymous()
    {
        // With [Authorize] the 401 landed in the authorization filter, BEFORE the action body and
        // therefore before the cookie was cleared. An idle tab past the access token's hour would show
        // the user signed out while the refresh cookie sat untouched — and the next page load restored
        // the session from it. The refresh token is the credential here; no other authority is needed.
        var method = typeof(AuthController).GetMethod(nameof(AuthController.Logout))!;
        Assert.NotNull(method.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Null(method.GetCustomAttribute<AuthorizeAttribute>());
    }

    [Fact]
    public async Task Logout_SendsTheCookieToken_SoTheSessionRowIsRevoked()
    {
        // The cookie now reaches this endpoint (Path=/api/auth). It previously did not, so the handler
        // received nothing and returned early — a web sign-out left a live 30-day row in the database
        // for a session the user believed they had ended.
        var (controller, context, mediator) = BuildWithMediator();
        context.Request.Headers["X-Tenant"] = "aqua";
        context.Request.Headers.Cookie = "roc_rt_aqua=aqua-token";

        await controller.Logout(null, default);

        Assert.Equal("aqua-token", Assert.IsType<LogoutCommand>(mediator.LastRequest).RefreshToken);
    }

    [Fact]
    public async Task Logout_ClearsEveryCookieShape_AtEveryPathTheyWereWrittenTo()
    {
        // Clearing one shape and leaving the others is a sign-out that does not sign out: the next
        // read finds a session the user thought they had ended.
        var (controller, context) = Build(native: false);
        context.Request.Headers["X-Tenant"] = "aqua";

        await controller.Logout(null, default);

        var cookies = AllSetCookies(context);
        Assert.Contains("roc_rt_aqua=;", cookies);              // current shape
        Assert.Contains("refresh_token_aqua=;", cookies);       // per-workspace, old path
        Assert.Contains("refresh_token=;", cookies);            // the original single cookie
        Assert.Contains("path=/api/auth/refresh", cookies, StringComparison.OrdinalIgnoreCase);
    }

    // ─── transitional: cookies issued before the rename ───────────────────

    [Fact]
    public async Task Refresh_FallsBackToThePerWorkspacePreDeployCookie()
    {
        var (controller, context, mediator) = BuildWithMediator();
        context.Request.Headers["X-Tenant"] = "aqua";
        context.Request.Headers.Cookie = "refresh_token_aqua=scoped-legacy";

        Assert.IsType<OkObjectResult>(await controller.Refresh(null, default));
        Assert.Equal("scoped-legacy", Assert.IsType<RefreshCmd>(mediator.LastRequest).RefreshToken);
    }

    [Fact]
    public async Task Refresh_FallsBackToTheOriginalUnnamedCookie()
    {
        // TRANSITIONAL, with the fallback it covers: without this, the deploy that introduced
        // per-workspace cookies signs out everyone currently using the web.
        var (controller, context, mediator) = BuildWithMediator();
        context.Request.Headers["X-Tenant"] = "aqua";
        context.Request.Headers.Cookie = "refresh_token=legacy-token";

        Assert.IsType<OkObjectResult>(await controller.Refresh(null, default));
        Assert.Equal("legacy-token", Assert.IsType<RefreshCmd>(mediator.LastRequest).RefreshToken);
    }

    [Fact]
    public async Task Refresh_PrefersTheCurrentCookie_OverAPreDeployOne()
    {
        // Both in the jar during the transition. The old one is a token that was already rotated
        // away, and presenting it would look exactly like a replay — which revokes the device.
        var (controller, context, mediator) = BuildWithMediator();
        context.Request.Headers["X-Tenant"] = "aqua";
        context.Request.Headers.Cookie = "refresh_token_aqua=stale; roc_rt_aqua=current";

        await controller.Refresh(null, default);

        Assert.Equal("current", Assert.IsType<RefreshCmd>(mediator.LastRequest).RefreshToken);
    }

    [Fact]
    public async Task Login_RetiresThePreDeployCookieWhileWritingTheNewOne()
    {
        var (controller, context) = Build(native: false);
        context.Request.Headers["X-Tenant"] = "aqua";

        await controller.Login(new LoginRequest("owner@aqua.in", "pw"), default);

        var cookies = AllSetCookies(context);
        Assert.Contains("roc_rt_aqua=user-id.random-secret", cookies);
        Assert.Contains("refresh_token_aqua=;", cookies);
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
        var (controller, context, mediator) = BuildWithMediator();
        context.Request.Headers.Cookie = "roc_rt=cookie-token";

        var result = await controller.Refresh(new RefreshRequest("body-token"), default);

        Assert.IsType<OkObjectResult>(result);
        // A browser's own HttpOnly cookie must not be overridable by request content.
        Assert.Equal("cookie-token", Assert.IsType<RefreshCmd>(mediator.LastRequest).RefreshToken);
    }

    [Fact]
    public async Task Refresh_WithNeitherCookieNorBody_Is401()
    {
        var (controller, _) = Build(native: true);

        var result = await controller.Refresh(null, default);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Contains("NO_REFRESH_TOKEN", unauthorized.Value!.ToString());
    }
}
