using Microsoft.EntityFrameworkCore;
using ROCloud.Domain.Entities.Platform;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;

namespace ROCloud.Application.Tests.Subscriptions;

/// <summary>
/// PlanType's DECLARATION ORDER is the tier ranking — RequirePlanAttribute gates features with
/// <c>tier &lt; _minimumPlan</c>, so a member's ordinal decides what that plan unlocks.
///
/// That makes adding a tier a security change, not a cosmetic one. Appending Starter to the end of
/// the enum (the obvious thing to do) would have ranked the ₹499 plan ABOVE Enterprise and handed
/// every plan-gated feature to the cheapest tier. These tests exist so that mistake fails the build
/// rather than shipping.
/// </summary>
public class PlanTierOrderTests
{
    [Fact]
    public void Starter_RanksBelowEveryOtherTier()
    {
        // The regression that matters: Starter is the cheapest plan, so it must unlock nothing.
        Assert.True(PlanType.Starter < PlanType.Basic);
        Assert.True(PlanType.Starter < PlanType.Pro);
        Assert.True(PlanType.Starter < PlanType.Enterprise);
    }

    [Fact]
    public void Tiers_AscendInPriceOrder()
    {
        Assert.Equal(
            [PlanType.Starter, PlanType.Basic, PlanType.Pro, PlanType.Enterprise],
            Enum.GetValues<PlanType>().OrderBy(p => p).ToArray());
    }

    [Theory]
    // A Starter tenant clears only a Starter gate.
    [InlineData(PlanType.Starter, PlanType.Starter, true)]
    [InlineData(PlanType.Starter, PlanType.Basic, false)]
    [InlineData(PlanType.Starter, PlanType.Pro, false)]
    [InlineData(PlanType.Starter, PlanType.Enterprise, false)]
    // Adding a tier below Basic must not change what the paid tiers already unlock.
    [InlineData(PlanType.Basic, PlanType.Starter, true)]
    [InlineData(PlanType.Basic, PlanType.Pro, false)]
    [InlineData(PlanType.Pro, PlanType.Pro, true)]
    [InlineData(PlanType.Enterprise, PlanType.Pro, true)]
    public void RequirePlanComparison_MatchesTheTierLadder(PlanType held, PlanType required, bool allowed)
    {
        // Mirrors RequirePlanAttribute's check: blocked when tier < minimum.
        Assert.Equal(allowed, !(held < required));
    }

    [Fact]
    public void PlanType_IsPersistedByName_WhichIsWhatMakesReorderingSafe()
    {
        // Reordering the enum only stays safe while the DB stores the NAME. If this ever became an
        // int column, inserting Starter at ordinal 0 would silently re-tier every existing plan row.
        using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"plantier-{Guid.NewGuid()}").Options,
            new TenantContext { TenantId = Guid.NewGuid() });

        var property = db.Model.FindEntityType(typeof(Plan))!.FindProperty(nameof(Plan.PlanType))!;

        Assert.Equal(typeof(string), property.GetProviderClrType());
    }
}
