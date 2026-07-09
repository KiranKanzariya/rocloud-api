using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Invoices;

/// <summary>
/// Computes a customer's standing (recurring) invoice discount as an absolute ₹ amount off the
/// subtotal (guide §26). Percentage is clamped to 0–100; Fixed is floored at 0.
/// </summary>
public static class CustomerDiscountCalculator
{
    public static decimal Compute(CustomerDiscountType type, decimal value, decimal subTotal) =>
        type switch
        {
            CustomerDiscountType.Percentage => Math.Round(subTotal * (Math.Clamp(value, 0m, 100m) / 100m), 2),
            CustomerDiscountType.Fixed => Math.Max(0m, value),
            _ => 0m
        };
}
