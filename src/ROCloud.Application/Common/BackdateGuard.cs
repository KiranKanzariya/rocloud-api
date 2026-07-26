using ROCloud.Application.Common.Exceptions;

namespace ROCloud.Application.Common;

/// <summary>
/// Enforces the platform backdating window (Billing:BackdateWindowDays) for manually entered business
/// dates — an order's OrderDate, a payment's PaidOn, a jar return's ReturnedOn. A supplied date may be no
/// earlier than <c>windowDays</c> days before today (app zone). A null date means the caller omitted it
/// (defaults to today) and always passes. This is the single source of truth so the three entry points
/// can't drift; the always-on "already-invoiced period" reject for orders is a separate rule (see
/// BackdatedOrderGuard).
/// <para>
/// The future is rejected by default — you cannot receive money or jars tomorrow. Orders are the
/// exception (<paramref name="allowFuture"/> = true): a future OrderDate is a legitimate advance booking
/// (event/program), surfaced on the delivery board when its day arrives.
/// </para>
/// </summary>
public static class BackdateGuard
{
    public static void Validate(
        DateOnly? date, int windowDays, string field, bool allowFuture = false, DateTime? utcNow = null)
    {
        if (date is not { } d) return;

        var today = AppTimeZone.Today(utcNow ?? DateTime.UtcNow);
        var window = Math.Max(0, windowDays);
        var earliest = today.AddDays(-window);

        if (!allowFuture && d > today)
            throw Fail(field, "A future date is not allowed.");

        if (d < earliest)
            throw Fail(field,
                $"You can only backdate up to {window} day(s). The earliest allowed date is {earliest:dd MMM yyyy}.");
    }

    private static ValidationException Fail(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}
