using ROCloud.Application.Features.Subscription;

namespace ROCloud.Application.Tests.Subscriptions;

/// <summary>
/// The plan-change promise: <b>changing your plan changes what you get, not how long you have.</b>
///
/// Worked example throughout — a tenant on Basic ₹1,099, cycle 8 Jul → 8 Aug (31 days), changing plan
/// on 27 Jul with 12 days left. Pro is ₹2,499 and Enterprise ₹5,999.
///
/// Before this split, a plan change ran through the RENEWAL path: it charged the full plan price and
/// pushed the end date out a whole cycle. Changing tier twice in one afternoon silently bought two
/// extra months, and left the tenant on the dearer plan while an invoice for the cheaper one covered
/// the period — ₹3,500 of revenue quietly lost on the example below.
/// </summary>
public class PlanChangeCalculatorTests
{
    private const decimal Basic = 1099m;
    private const decimal Pro = 2499m;
    private const decimal Enterprise = 5999m;

    private static readonly DateTime CycleEnd = new(2026, 8, 8, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ChangesOn = new(2026, 7, 27, 0, 0, 0, DateTimeKind.Utc);

    private static PlanChange Change(decimal oldNet, decimal newNet, DateTime? end = null, DateTime? now = null)
        => PlanChangeCalculator.Decide(end ?? CycleEnd, oldNet, newNet, yearly: false, now ?? ChangesOn);

    [Fact]
    public void Upgrade_ChargesOnlyTheDifference_ForOnlyTheDaysRemaining()
    {
        var change = Change(Basic, Pro);

        Assert.Equal(PlanChangeKind.Upgrade, change.Kind);
        Assert.Equal(12, change.RemainingDays);
        Assert.Equal(31, change.CycleDays);
        // (2499 − 1099) × 12/31 = ₹541.94 — NOT the full ₹2,499 the old path charged.
        Assert.Equal(541.94m, change.Amount);
    }

    [Fact]
    public void Upgrade_DoesNotDependOnTheRouteTaken()
    {
        // Basic → Pro → Enterprise on the same day must cost the same as Basic → Enterprise directly,
        // or a customer could gain (or lose) money purely by clicking through an intermediate tier.
        var viaPro = Change(Basic, Pro).Amount + Change(Pro, Enterprise).Amount;
        var direct = Change(Basic, Enterprise).Amount;

        Assert.Equal(direct, viaPro, precision: 1);   // ₹1,896.77 vs ₹1,896.78 — one paisa of rounding
    }

    [Fact]
    public void Downgrade_IsNeverCharged()
    {
        var change = Change(Enterprise, Basic);

        Assert.Equal(PlanChangeKind.Downgrade, change.Kind);
        Assert.Equal(0m, change.Amount);
    }

    [Fact]
    public void SameNetPrice_IsALateralMove_AndCostsNothing()
    {
        // Two plans that cost this tenant the same (e.g. both fully discounted) — switch, don't charge.
        var change = Change(Pro, Pro);

        Assert.Equal(PlanChangeKind.Lateral, change.Kind);
        Assert.Equal(0m, change.Amount);
    }

    [Fact]
    public void NoLiveTerm_BuysAFullCycle_NotAProratedSlice()
    {
        // First purchase / straight off trial: there is no paid time to prorate against.
        var change = PlanChangeCalculator.Decide(
            currentEnd: null, oldNet: 0m, newNet: Pro, yearly: false, now: ChangesOn);

        Assert.Equal(PlanChangeKind.NewTerm, change.Kind);
        Assert.Equal(Pro, change.Amount);
    }

    [Fact]
    public void LapsedTenant_BuysAFullCycle_RatherThanProratingAgainstExpiredTime()
    {
        // Term ended 8 Jul; they pay on 27 Jul. Nothing is left to prorate — this is a renewal.
        var change = Change(Basic, Pro, end: new DateTime(2026, 7, 8, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(PlanChangeKind.NewTerm, change.Kind);
        Assert.Equal(Pro, change.Amount);
    }

    [Fact]
    public void UpgradingOnTheLastDay_StillBillsThatDay()
    {
        // Ceiling, not floor: they get the better plan today, so today is charged. A floor would hand
        // out a free upgrade day and round the charge to ₹0.
        var change = Change(Basic, Pro, now: CycleEnd.AddHours(-6));

        Assert.Equal(1, change.RemainingDays);
        Assert.Equal(45.16m, change.Amount);   // 1400 × 1/31
    }

    [Fact]
    public void ProratedDelta_CanFallBelowRazorpaysMinimum_AndIsThenFree()
    {
        // A ₹20/cycle difference with one day left is 65 paise. Razorpay rejects orders under ₹1, and
        // refusing a legitimate plan change over 65 paise would be absurd — initiate and complete both
        // treat this as free via MinChargeableAmount.
        var change = Change(1000m, 1020m, now: CycleEnd.AddHours(-6));

        Assert.Equal(0.65m, change.Amount);
        Assert.True(change.Amount < PlanChangeCalculator.MinChargeableAmount);
    }

    [Fact]
    public void Prorate_IsZeroWhenNoDaysRemain()
    {
        Assert.Equal(0m, PlanChangeCalculator.Prorate(1400m, remainingDays: 0, cycleDays: 31));
        Assert.Equal(0m, PlanChangeCalculator.Prorate(1400m, remainingDays: 12, cycleDays: 0));
    }

    [Fact]
    public void YearlyCycle_ProratesOverTheYear_NotAMonth()
    {
        var yearEnd = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var change = PlanChangeCalculator.Decide(
            yearEnd, oldNet: 10000m, newNet: 22000m, yearly: true,
            now: new DateTime(2026, 12, 2, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(PlanChangeKind.Upgrade, change.Kind);
        Assert.Equal(365, change.CycleDays);
        Assert.Equal(30, change.RemainingDays);
        Assert.Equal(986.30m, change.Amount);   // 12000 × 30/365
    }
}
