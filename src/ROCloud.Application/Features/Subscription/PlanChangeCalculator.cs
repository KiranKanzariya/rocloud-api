namespace ROCloud.Application.Features.Subscription;

/// <summary>What a plan change does, decided once and reused by initiate and complete.</summary>
public enum PlanChangeKind
{
    /// <summary>No prior paid term (first purchase, trial, or lapsed) — buy a full cycle of the new plan.</summary>
    NewTerm,

    /// <summary>Mid-cycle move to a dearer plan — charge the prorated difference, term unchanged.</summary>
    Upgrade,

    /// <summary>Mid-cycle move to a cheaper plan — charge nothing, apply at period end.</summary>
    Downgrade,

    /// <summary>Mid-cycle, same net price (e.g. both fully discounted) — switch for free, term unchanged.</summary>
    Lateral,
}

/// <summary>The decision plus the money, for one plan change.</summary>
/// <param name="Amount">₹ to charge NOW. Zero for a downgrade or a lateral move.</param>
/// <param name="RemainingDays">Days left in the current paid cycle (0 when there is no live term).</param>
/// <param name="CycleDays">Length of the current paid cycle in days (0 when there is no live term).</param>
public sealed record PlanChange(
    PlanChangeKind Kind,
    decimal Amount,
    int RemainingDays,
    int CycleDays);

/// <summary>
/// Works out what changing plan costs mid-cycle (industry-standard proration).
///
/// <b>The rule: changing your plan changes what you get, not how long you have.</b> An upgrade is
/// charged only for the days remaining, and only for the DIFFERENCE in price — the customer already
/// paid for those days at the old rate. The renewal date never moves.
///
/// This deliberately differs from <see cref="Services.SubscriptionTermCalculator"/>, which governs
/// RENEWAL (paying for another cycle). Both are correct; they answer different questions. Before this
/// split, a plan change ran through the renewal path and so bought a whole extra cycle — changing tier
/// three times in one afternoon silently purchased three months, and left the tenant on the dearer plan
/// while an invoice for the cheaper one covered the period.
///
/// A downgrade is never charged and never refunded: it takes effect at period end, so the tenant keeps
/// the plan they already paid for until then. This is what every major billing system does, and it
/// avoids credit balances entirely.
/// </summary>
public static class PlanChangeCalculator
{
    /// <summary>
    /// Smallest amount worth sending to Razorpay. Its minimum order is ₹1.00, and a prorated delta can
    /// legitimately round below that (a cheap tier change with a day left). Anything under this is
    /// applied for free rather than failing — initiate and complete MUST use the same threshold, or one
    /// creates no order while the other demands one.
    /// </summary>
    public const decimal MinChargeableAmount = 1m;

    /// <summary>
    /// Decides the change. <paramref name="currentEnd"/> is the tenant's SubscriptionEndsAt;
    /// <paramref name="oldNet"/>/<paramref name="newNet"/> are the tenant's net (post-discount) prices
    /// for the current and target plan on the same billing cycle.
    /// </summary>
    public static PlanChange Decide(
        DateTime? currentEnd, decimal oldNet, decimal newNet, bool yearly, DateTime now)
    {
        // No live paid term — nothing to prorate against, so this is a purchase of a fresh cycle.
        // Covers first purchase, a tenant coming off trial, and anyone already lapsed.
        if (currentEnd is not { } end || end <= now)
            return new PlanChange(PlanChangeKind.NewTerm, newNet, 0, 0);

        var cycleStart = yearly ? end.AddYears(-1) : end.AddMonths(-1);
        var cycleDays = Math.Max(1, (int)Math.Round((end - cycleStart).TotalDays));
        // Ceiling, not floor: a customer upgrading with a few hours left still gets today, so bill it.
        var remainingDays = Math.Clamp((int)Math.Ceiling((end - now).TotalDays), 0, cycleDays);

        if (newNet > oldNet)
        {
            var delta = Prorate(newNet - oldNet, remainingDays, cycleDays);
            return new PlanChange(PlanChangeKind.Upgrade, delta, remainingDays, cycleDays);
        }

        if (newNet < oldNet)
            return new PlanChange(PlanChangeKind.Downgrade, 0m, remainingDays, cycleDays);

        return new PlanChange(PlanChangeKind.Lateral, 0m, remainingDays, cycleDays);
    }

    /// <summary>
    /// The part of <paramref name="fullCycleAmount"/> owed for the days remaining, rounded to paise.
    /// Kept separate so the arithmetic is directly testable.
    /// </summary>
    public static decimal Prorate(decimal fullCycleAmount, int remainingDays, int cycleDays)
    {
        if (cycleDays <= 0 || remainingDays <= 0) return 0m;
        var share = fullCycleAmount * remainingDays / cycleDays;
        return Math.Round(Math.Max(0m, share), 2, MidpointRounding.AwayFromZero);
    }
}
