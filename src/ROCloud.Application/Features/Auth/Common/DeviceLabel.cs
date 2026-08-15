using System.Text.RegularExpressions;

namespace ROCloud.Application.Features.Auth.Common;

/// <summary>
/// Works out what to call the device a sign-in came from, for the "Signed-in devices" list.
/// </summary>
/// <remarks>
/// Best-effort and cosmetic — nothing authorises on it, and a client is free to send whatever it likes
/// in <c>X-Device</c>. Its only job is to let an owner looking at a list of their own sessions answer
/// "is one of these not mine?", which a column of UUIDs cannot.
/// <para>
/// Deliberately coarse: "Chrome on Windows", not a full user-agent string. A precise fingerprint would
/// be more identifying than the question needs, and it would sit in the database indefinitely.
/// </para>
/// </remarks>
public static partial class DeviceLabel
{
    /// <summary>Sent by the Flutter app, which knows its own hardware better than any header parse.</summary>
    public const string Header = "X-Device";

    private const int MaxLength = 80;

    /// <summary>
    /// <paramref name="supplied"/> (the <c>X-Device</c> header) wins when present; otherwise the
    /// user-agent is reduced to a browser and platform. Null when neither says anything useful.
    /// </summary>
    public static string? From(string? supplied, string? userAgent)
    {
        if (!string.IsNullOrWhiteSpace(supplied)) return Clean(supplied);
        if (string.IsNullOrWhiteSpace(userAgent)) return null;

        var browser = Browser(userAgent);
        var platform = Platform(userAgent);

        return (browser, platform) switch
        {
            (null, null) => null,
            (null, { } p) => p,
            ({ } b, null) => b,
            var (b, p) => $"{b} on {p}"
        };
    }

    /// <summary>Order matters: Edge and Opera both claim to be Chrome, and Chrome claims to be Safari.</summary>
    private static string? Browser(string agent) =>
        agent.Contains("Edg/", StringComparison.OrdinalIgnoreCase) ? "Edge"
        : agent.Contains("OPR/", StringComparison.OrdinalIgnoreCase) ? "Opera"
        : agent.Contains("Firefox/", StringComparison.OrdinalIgnoreCase) ? "Firefox"
        : agent.Contains("Chrome/", StringComparison.OrdinalIgnoreCase) ? "Chrome"
        : agent.Contains("Safari/", StringComparison.OrdinalIgnoreCase) ? "Safari"
        : null;

    private static string? Platform(string agent) =>
        agent.Contains("Android", StringComparison.OrdinalIgnoreCase) ? "Android"
        : agent.Contains("iPhone", StringComparison.OrdinalIgnoreCase) ? "iPhone"
        : agent.Contains("iPad", StringComparison.OrdinalIgnoreCase) ? "iPad"
        : agent.Contains("Windows", StringComparison.OrdinalIgnoreCase) ? "Windows"
        : agent.Contains("Mac OS X", StringComparison.OrdinalIgnoreCase) ? "Mac"
        : agent.Contains("Linux", StringComparison.OrdinalIgnoreCase) ? "Linux"
        : null;

    /// <summary>
    /// Strips control characters and angle brackets, then trims to length. The value is
    /// client-supplied and is rendered in both portals and written to logs, so it must not be able to
    /// carry a newline into a log line or markup into a list.
    /// </summary>
    private static string? Clean(string value)
    {
        var collapsed = Unsafe().Replace(value, " ").Trim();
        if (collapsed.Length == 0) return null;
        return collapsed.Length <= MaxLength ? collapsed : collapsed[..MaxLength];
    }

    [GeneratedRegex(@"[\p{C}<>]+")]
    private static partial Regex Unsafe();
}
