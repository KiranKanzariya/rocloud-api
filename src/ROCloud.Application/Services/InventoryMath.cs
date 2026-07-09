using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Services;

/// <summary>
/// The single source of truth for how a movement type affects an inventory row's counters.
/// Used by both <see cref="InventoryService"/> (order/delivery-driven) and the manual
/// AddInventoryMovement command so the two never diverge.
/// availableStock (computed elsewhere) = total - issued - damaged.
/// </summary>
internal static class InventoryMath
{
    /// <param name="fromCustomer">
    /// True when the movement reflects a jar coming back from a customer (i.e. it carries a
    /// CustomerId). Only matters for <see cref="InventoryMovementType.Damage"/>: a jar returned
    /// broken leaves the customer's hands (issued−) AND is written off (damaged+), whereas a
    /// warehouse breakage write-off only touches damaged stock.
    /// </param>
    public static void Apply(Inventory inv, InventoryMovementType type, int quantity, bool fromCustomer = false)
    {
        switch (type)
        {
            case InventoryMovementType.Issue:
                inv.IssuedStock += quantity;
                break;
            case InventoryMovementType.Return:
                inv.IssuedStock -= quantity;
                inv.ReturnedStock += quantity;
                break;
            case InventoryMovementType.Damage:
                inv.DamagedStock += quantity;
                if (fromCustomer) inv.IssuedStock -= quantity; // a damaged jar came back, just unusable
                break;
            case InventoryMovementType.Restock:
                inv.TotalStock += quantity;
                break;
            case InventoryMovementType.Adjustment:
                inv.TotalStock += quantity;   // signed: callers may pass a negative quantity
                break;
        }
        inv.LastUpdated = DateTime.UtcNow;
    }
}
