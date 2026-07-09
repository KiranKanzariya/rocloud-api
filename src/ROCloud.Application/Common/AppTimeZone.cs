namespace ROCloud.Application.Common;

/// <summary>
/// The application's display/business timezone, configured once at startup from <c>App:TimeZone</c>
/// (default IST). ROCloud is India-first (guide §1), so the business "today" used for delivery-day
/// logic and the wall-clock for recurring jobs is this zone — not UTC. Timestamps are still stored
/// as UTC; this only decides how a UTC instant maps to a calendar day / wall-clock time, and it is
/// the single source of truth shared with the DB session timezone (App:TimeZone) and the portals'
/// display offset (environment.timeZoneOffset).
///
/// <see cref="Configure"/> is called from the composition root (Program.cs), so the Application layer
/// never reads IConfiguration directly. Set once at startup; reads are lock-free thereafter.
/// </summary>
public static class AppTimeZone
{
    private static TimeZoneInfo _current = Resolve("Asia/Kolkata");

    /// <summary>The configured timezone (default IST until <see cref="Configure"/> runs).</summary>
    public static TimeZoneInfo Current => _current;

    /// <summary>Set the app timezone from an IANA or Windows id (App:TimeZone). No-op if null/blank.</summary>
    public static void Configure(string? timeZoneId)
    {
        if (!string.IsNullOrWhiteSpace(timeZoneId))
            _current = Resolve(timeZoneId!);
    }

    /// <summary>The calendar day in the configured zone for the given UTC instant.</summary>
    public static DateOnly Today(DateTime utcNow) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utcNow, _current));

    private static TimeZoneInfo Resolve(string id)
    {
        // .NET 6+ resolves both IANA ("Asia/Kolkata") and Windows ("India Standard Time") ids on every
        // OS via ICU. Try the id as given, then cross-map IANA<->Windows in case the host only ships one
        // form, so a valid config value never silently falls back.
        if (TryFind(id, out var tz)) return tz;
        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var win) && TryFind(win, out tz)) return tz;
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var iana) && iana is not null && TryFind(iana, out tz)) return tz;
        // Last-resort fixed IST (India observes no DST) so a broken host tz database never drops us to UTC.
        return TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromHours(5.5), "India Standard Time", "IST");
    }

    private static bool TryFind(string id, out TimeZoneInfo tz)
    {
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(id); return true; }
        catch (TimeZoneNotFoundException) { tz = null!; return false; }
        catch (InvalidTimeZoneException) { tz = null!; return false; }
    }
}
