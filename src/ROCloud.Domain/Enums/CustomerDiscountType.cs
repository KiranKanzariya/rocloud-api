namespace ROCloud.Domain.Enums;

/// <summary>
/// A customer's standing (recurring) invoice discount, set by platform staff (guide §26).
/// DB: customers.discount_type. None = no discount; Percentage = discount_value % off the
/// subtotal; Fixed = discount_value ₹ off the subtotal.
/// </summary>
public enum CustomerDiscountType
{
    None,
    Percentage,
    Fixed
}
