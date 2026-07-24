namespace ROCloud.Application.Features.Subscription.Services;

/// <summary>
/// Works out the new <c>SubscriptionEndsAt</c> when a tenant pays. The rule is one sentence:
/// <b>₹X buys one cycle of USABLE access, whenever you pay.</b>
///
/// Two halves, and both are needed:
///  1. <b>Charge for the grace days they used.</b> After the period ends, TenantMiddleware keeps the app
///     fully working for Subscription:OverdueGraceDays — real access, billed to nobody. So the new term
///     runs from the OLD end date, not from the payment date. Without this a tenant who is a few days
///     late every month collects those free days forever (7 days late every cycle ≈ two free months a
///     year), and the leak is invisible in reporting because it just looks like a renewal.
///  2. <b>Give back the days they were locked out.</b> Once past grace the app is dead to them, so
///     charging for that window would be selling nothing. Those days are added on the end.
///
/// Half 1 alone would be harsh (a tenant paying three weeks late would buy a term that had already
/// expired); half 2 alone is today's behaviour. Together, every payment yields the same usable term.
/// </summary>
public static class SubscriptionTermCalculator
{
    /// <summary>
    /// The tenant's new subscription end date.
    /// </summary>
    /// <param name="currentEnd">
    /// Existing <c>SubscriptionEndsAt</c>. Null for a first purchase or a tenant coming straight off a
    /// trial — there is no prior paid term to extend, so the new one starts now.
    /// </param>
    /// <param name="yearly">Yearly cycle rather than monthly.</param>
    /// <param name="overdueGraceDays">Subscription:OverdueGraceDays — must match TenantMiddleware, or
    /// this would credit days the tenant could actually use (or bill for days it had blocked).</param>
    /// <param name="now">Current UTC time (injected so the maths is testable).</param>
    public static DateTime NextEnd(DateTime? currentEnd, bool yearly, int overdueGraceDays, DateTime now)
    {
        DateTime OneCycleFrom(DateTime from) => yearly ? from.AddYears(1) : from.AddMonths(1);

        // No prior paid term (first purchase, or lapsed straight from trial) → start from today.
        if (currentEnd is not { } end) return OneCycleFrom(now);

        // Renewing early, or still inside the paid period → stack the new cycle on the end so no
        // already-paid day is lost.
        if (end > now) return OneCycleFrom(end);

        // Lapsed. The term runs from the old end date (half 1), then the locked-out window is handed
        // back (half 2). Grace days fall between the two and are therefore paid for — which is right,
        // the tenant had the whole product during them.
        var blockedFrom = end.AddDays(overdueGraceDays);
        var lockedOut = now > blockedFrom ? now - blockedFrom : TimeSpan.Zero;
        return OneCycleFrom(end) + lockedOut;
    }

    /// <summary>
    /// Start of the term <see cref="NextEnd"/> produces — the old end date, or today when there is no
    /// prior term. Pair the two when writing an invoice so the stated period is the period received.
    /// </summary>
    public static DateTime TermStart(DateTime? currentEnd, DateTime now) => currentEnd ?? now;
}
