namespace ROCloud.Domain.Enums;

/// <summary>
/// A tenant's standing (recurring) discount on their ROCloud subscription price, set by platform
/// staff (guide §26). DB: tenants.subscription_discount_type. None = pays full plan price;
/// Percentage = subscription_discount_value % off the plan price; Fixed = that many ₹ off.
/// Separate from <see cref="CustomerDiscountType"/>, which discounts a tenant's own customers' bills.
/// </summary>
public enum SubscriptionDiscountType
{
    None,
    Percentage,
    Fixed
}
