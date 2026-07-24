using ROCloud.Application.Features.Subscription.Services;

namespace ROCloud.Application.Tests.Subscriptions;

/// <summary>
/// The billing promise: <b>one payment buys one cycle of USABLE access, whenever it is made.</b>
///
/// Worked example throughout — Akash, Basic ₹999, paid period 1 Jul → 1 Aug, 7 grace days so the app
/// keeps working until 8 Aug and is blocked from then on.
///
/// Before this rule the term always restarted at the payment date, so the free grace days were never
/// billed. A tenant a few days late EVERY month collected them every cycle (7 days late ≈ two free
/// months a year), and it was invisible in reporting because it just looked like a renewal.
/// </summary>
public class SubscriptionTermCalculatorTests
{
    private const int GraceDays = 7;
    private static readonly DateTime PeriodEnd = new(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);

    private static DateTime PaysOn(int augustDay, DateTime? currentEnd = null)
        => SubscriptionTermCalculator.NextEnd(
            currentEnd ?? PeriodEnd, yearly: false, GraceDays,
            new DateTime(2026, 8, augustDay, 0, 0, 0, DateTimeKind.Utc));

    /// <summary>Days the tenant can actually USE the app between paying and the new end date.</summary>
    private static double UsableDaysAfterPaying(DateTime paidOn, DateTime newEnd) => (newEnd - paidOn).TotalDays;

    [Fact]
    public void PayingEarly_StacksOnTheEnd_SoNoPaidDayIsLost()
    {
        // 28 Jul — still inside the paid period. The new cycle must start at 1 Aug, not at 28 Jul,
        // or the tenant would forfeit the 4 days they had already bought.
        var end = SubscriptionTermCalculator.NextEnd(
            PeriodEnd, yearly: false, GraceDays, new DateTime(2026, 7, 28, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), end);
    }

    [Fact]
    public void PayingOnTheDay_RunsAFullCycleFromTheOldEnd()
    {
        Assert.Equal(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), PaysOn(1));
    }

    [Fact]
    public void PayingInsideGrace_BillsTheGraceDaysAlreadyUsed()
    {
        // 4 Aug: the app has been fully working (free) since 1 Aug. The new term runs 1 Aug → 1 Sep,
        // so those 3 days are paid for. Previously this returned 4 Sep — 3 free days, every cycle.
        Assert.Equal(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), PaysOn(4));
    }

    [Fact]
    public void PayingAfterTheBlock_HandsBackEveryLockedOutDay()
    {
        // 27 Aug: used 1–8 Aug (grace, billed), locked out 8–27 Aug = 19 days (credited).
        // 1 Sep + 19 = 20 Sep.
        Assert.Equal(new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc), PaysOn(27));
    }

    [Fact]
    public void PayingMonthsLate_StillGetsAWholeCycleOfUsableAccess()
    {
        // 1 Nov, long suspended. Locked out 8 Aug → 1 Nov = 85 days, so the term ends 25 Nov —
        // NOT 1 Sep, which would sell them a period that had already elapsed.
        var end = SubscriptionTermCalculator.NextEnd(
            PeriodEnd, yearly: false, GraceDays, new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new DateTime(2026, 11, 25, 0, 0, 0, DateTimeKind.Utc), end);
    }

    [Theory]
    [InlineData(4)]    // inside grace
    [InlineData(8)]    // the moment the block arms
    [InlineData(27)]   // long past the block
    public void WheneverTheyPay_TheUsableTermIsTheSameLength(int augustDay)
    {
        // The whole point, asserted directly: pay on any day and you get the same amount of working
        // app. Only the dates move. (Grace days are consumed before paying, hence 31 − 7 = 24.)
        var paidOn = new DateTime(2026, 8, augustDay, 0, 0, 0, DateTimeKind.Utc);
        var graceUsed = Math.Min(GraceDays, (paidOn - PeriodEnd).TotalDays);

        var usable = UsableDaysAfterPaying(paidOn, PaysOn(augustDay)) + graceUsed;

        Assert.Equal(31, usable, precision: 6);   // 1 Aug → 1 Sep
    }

    [Fact]
    public void ANewSubscription_StartsToday_BecauseThereIsNoPriorTermToExtend()
    {
        // First purchase, or a tenant coming straight off a trial — SubscriptionEndsAt is null.
        var now = new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc);

        var end = SubscriptionTermCalculator.NextEnd(null, yearly: false, GraceDays, now);

        Assert.Equal(new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc), end);
    }

    [Fact]
    public void Yearly_UsesAYearCycle_AndCreditsLockedOutDaysTheSameWay()
    {
        var end = SubscriptionTermCalculator.NextEnd(
            PeriodEnd, yearly: true, GraceDays, new DateTime(2026, 8, 27, 0, 0, 0, DateTimeKind.Utc));

        // 1 Aug 2027 + the same 19 locked-out days.
        Assert.Equal(new DateTime(2027, 8, 20, 0, 0, 0, DateTimeKind.Utc), end);
    }

    [Fact]
    public void AnAdminSuspensionAlreadyCreditedBack_IsNotCreditedTwice()
    {
        // SetTenantStatusCommand credits days lost to an ACCIDENTAL suspension by pushing
        // SubscriptionEndsAt forward. This calculator then works off that already-extended date, so the
        // same window cannot be paid for twice: the block here is measured from the NEW end + grace.
        var creditedEnd = PeriodEnd.AddDays(10);                       // 11 Aug after the admin credit
        var paidOn = new DateTime(2026, 8, 27, 0, 0, 0, DateTimeKind.Utc);

        var end = SubscriptionTermCalculator.NextEnd(creditedEnd, yearly: false, GraceDays, paidOn);

        // Blocked only from 18 Aug (11 Aug + 7), so 9 locked-out days — not the 19 measured from 1 Aug.
        Assert.Equal(new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc).AddDays(9), end);
    }
}
