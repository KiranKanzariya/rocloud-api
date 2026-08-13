using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ROCloud.API.Middleware;

namespace ROCloud.API.Tests.Middleware;

public class AppVersionGateMiddlewareTests
{
    private static IConfiguration Config(int minSupportedBuild, int latestBuild = 0) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mobile:MinSupportedBuild"] = minSupportedBuild.ToString(),
                ["Mobile:LatestBuild"] = latestBuild.ToString(),
                ["Mobile:StoreUrl"] = "https://play.google.com/store/apps/details?id=in.rocloud.owner"
            })
            .Build();

    private static async Task<(DefaultHttpContext Context, bool Reached)> InvokeAsync(
        string? versionHeader, IConfiguration config, string path = "/api/customers")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        if (versionHeader is not null)
            context.Request.Headers["X-App-Version"] = versionHeader;

        var reached = false;
        var middleware = new AppVersionGateMiddleware(
            _ => { reached = true; return Task.CompletedTask; },
            NullLogger<AppVersionGateMiddleware>.Instance);

        await middleware.InvokeAsync(context, config);
        return (context, reached);
    }

    // ─── the web portal must be completely unaffected ─────────────────────

    [Fact]
    public async Task RequestWithoutVersionHeader_IsNeverGated()
    {
        // Both Angular portals send no X-App-Version. A floor set for the app must not touch them.
        var (context, reached) = await InvokeAsync(null, Config(minSupportedBuild: 500));

        Assert.True(reached);
        Assert.NotEqual(StatusCodes.Status426UpgradeRequired, context.Response.StatusCode);
    }

    // ─── the gate itself ──────────────────────────────────────────────────

    [Fact]
    public async Task BuildBelowFloor_Is426WithUpdateCode()
    {
        var (context, reached) = await InvokeAsync("1.2.0+12", Config(minSupportedBuild: 20));

        Assert.False(reached);
        Assert.Equal(StatusCodes.Status426UpgradeRequired, context.Response.StatusCode);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Contains("APP_UPDATE_REQUIRED", body);
        Assert.Contains("play.google.com", body);
    }

    [Theory]
    [InlineData("1.4.0+20")] // exactly at the floor
    [InlineData("1.5.0+21")] // above it
    public async Task BuildAtOrAboveFloor_PassesThrough(string header)
    {
        var (context, reached) = await InvokeAsync(header, Config(minSupportedBuild: 20));

        Assert.True(reached);
        Assert.NotEqual(StatusCodes.Status426UpgradeRequired, context.Response.StatusCode);
    }

    [Fact]
    public async Task FloorOfZero_BlocksNothing()
    {
        // The shipped default. Until a floor is deliberately set, the wall is inert.
        var (_, reached) = await InvokeAsync("0.1.0+1", Config(minSupportedBuild: 0));

        Assert.True(reached);
    }

    // ─── fail open ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("garbage")]
    [InlineData("1.4.0")] // name only, no build
    [InlineData("1.4.0+")] // trailing separator
    [InlineData("1.4.0+abc")]
    [InlineData("")]
    public async Task UnreadableVersionHeader_FailsOpen(string header)
    {
        // A parsing quirk must never lock a working app out of the API.
        var (_, reached) = await InvokeAsync(header, Config(minSupportedBuild: 500));

        Assert.True(reached);
    }

    // ─── the blocked app must still be able to get unstuck ────────────────

    [Theory]
    [InlineData("/api/app/version")]
    [InlineData("/api/health")]
    public async Task PolicyAndHealthEndpoints_StayReachableWhenBlocked(string path)
    {
        // Gating the endpoint that reports the gate would leave a walled app with nowhere to go:
        // no store URL, no message, no way forward.
        var (_, reached) = await InvokeAsync("1.0.0+1", Config(minSupportedBuild: 500), path);

        Assert.True(reached);
    }

    // ─── header parsing ───────────────────────────────────────────────────

    [Fact]
    public void ParseBuild_ReadsTheIntegerAfterThePlus()
    {
        Assert.Equal(42, AppVersionGateMiddleware.ParseBuild("1.4.0+42"));
        Assert.Equal(7, AppVersionGateMiddleware.ParseBuild("0.0.1+7"));
        Assert.Null(AppVersionGateMiddleware.ParseBuild(null));
        Assert.Null(AppVersionGateMiddleware.ParseBuild("1.4.0"));
    }

    [Fact]
    public void ParseBuild_OrdersByNumberNotByString()
    {
        var older = AppVersionGateMiddleware.ParseBuild("1.9.0+9")!.Value;
        var newer = AppVersionGateMiddleware.ParseBuild("1.10.0+10")!.Value;

        // As strings "1.10.0" sorts BELOW "1.9.0" — comparing names would invert the gate.
        Assert.True(newer > older);
    }
}
