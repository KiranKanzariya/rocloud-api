using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Subscription;

/// <summary>
/// Computes a tenant's standing discount on their ROCloud subscription price (guide §26).
/// Percentage is clamped to 0–100; Fixed is floored at 0; the discount never exceeds the price,
/// so the net amount is always ≥ 0.
/// </summary>
public static class SubscriptionDiscountCalculator
{
    /// <summary>The ₹ amount taken off <paramref name="price"/>, capped at the price.</summary>
    public static decimal Discount(SubscriptionDiscountType type, decimal value, decimal price)
    {
        var raw = type switch
        {
            SubscriptionDiscountType.Percentage => Math.Round(price * (Math.Clamp(value, 0m, 100m) / 100m), 2),
            SubscriptionDiscountType.Fixed => Math.Max(0m, value),
            _ => 0m
        };
        return Math.Min(raw, price);
    }

    /// <summary>The price the tenant actually pays after the discount (≥ 0).</summary>
    public static decimal Net(SubscriptionDiscountType type, decimal value, decimal price)
        => Math.Max(0m, price - Discount(type, value, price));
}
