namespace ROCloud.Domain.Enums;

/// <summary>
/// Subscription tier. DB: plans.plan_type (stored as the NAME, not the ordinal — see
/// PlanConfiguration's HasConversion&lt;string&gt;()).
///
/// DECLARATION ORDER IS THE TIER RANKING. RequirePlanAttribute compares tiers with
/// <c>tier &lt; _minimumPlan</c>, so a member's ordinal decides what it unlocks. A new tier must be
/// inserted at its true price position, never appended — appending Starter would have made it
/// outrank Enterprise and hand every plan-gated feature to the cheapest plan.
///
/// Reordering is safe for existing rows precisely because the DB stores the name.
/// </summary>
public enum PlanType
{
    Starter,
    Basic,
    Pro,
    Enterprise
}
