using System.Globalization;

namespace ROCloud.API.Middleware;

/// <summary>
/// Configured update policy for the mobile app, read from the <c>Mobile</c> section.
/// </summary>
public sealed record AppVersionPolicy(
    int MinSupportedBuild,
    int LatestBuild,
    string StoreUrl,
    string? UpdateMessage)
{
    public static AppVersionPolicy From(IConfiguration config)
    {
        var section = config.GetSection("Mobile");
        return new AppVersionPolicy(
            MinSupportedBuild: section.GetValue("MinSupportedBuild", 0),
            LatestBuild: section.GetValue("LatestBuild", 0),
            StoreUrl: section.GetValue("StoreUrl", string.Empty) ?? string.Empty,
            UpdateMessage: section.GetValue<string?>("UpdateMessage"));
    }
}

/// <summary>
/// Blocks mobile builds below the supported floor with <c>426 Upgrade Required</c>.
/// <para>
/// This exists because a published app can never be recalled: once it is on a phone, the only way
/// to stop an out-of-date build from talking to the API is for the API to refuse it. An app shipped
/// without a client that understands this response can never be forced to update at all.
/// </para>
/// <para>
/// It fails open in every ambiguous case — no header, an unparseable header, or no configured floor
/// all pass straight through. The web portal sends no <c>X-App-Version</c> and is never affected.
/// </para>
/// </summary>
public class AppVersionGateMiddleware
{
    public const string VersionHeader = "X-App-Version";

    /// <summary>
    /// Paths a blocked client must still reach. <c>/api/app</c> is the important one: it serves the
    /// store URL the update screen needs, so gating it would leave a walled app with nowhere to go.
    /// </summary>
    private static readonly string[] ExemptPrefixes =
    {
        "/api/app", "/api/health", "/health", "/swagger"
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<AppVersionGateMiddleware> _logger;

    public AppVersionGateMiddleware(RequestDelegate next, ILogger<AppVersionGateMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IConfiguration config)
    {
        var build = ParseBuild(context.Request.Headers[VersionHeader].FirstOrDefault());
        var path = context.Request.Path;

        if (build is null || IsExempt(path))
        {
            await _next(context);
            return;
        }

        var policy = AppVersionPolicy.From(config);
        if (policy.MinSupportedBuild <= 0 || build >= policy.MinSupportedBuild)
        {
            await _next(context);
            return;
        }

        _logger.LogInformation(
            "Blocked app build {Build}, below the supported floor {Floor}",
            build, policy.MinSupportedBuild);

        context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
        await context.Response.WriteAsJsonAsync(new
        {
            error = string.IsNullOrWhiteSpace(policy.UpdateMessage)
                ? "This version of ROCloud is no longer supported. Please update to continue."
                : policy.UpdateMessage,
            code = "APP_UPDATE_REQUIRED",
            minSupportedBuild = policy.MinSupportedBuild,
            latestBuild = policy.LatestBuild,
            storeUrl = policy.StoreUrl
        });
    }

    /// <summary>
    /// Reads the build number out of a <c>1.4.0+42</c> header.
    /// <para>
    /// Returns null for anything it cannot read — an absent header (a browser), a malformed one, or
    /// a bare version name. Null means "not a gated client" and the request proceeds: a parsing
    /// quirk must never lock a working app out of the API.
    /// </para>
    /// </summary>
    public static int? ParseBuild(string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return null;

        var separator = header.LastIndexOf('+');
        if (separator < 0 || separator == header.Length - 1) return null;

        return int.TryParse(
            header.AsSpan(separator + 1),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var build)
            ? build
            : null;
    }

    private static bool IsExempt(PathString path) =>
        ExemptPrefixes.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));
}
