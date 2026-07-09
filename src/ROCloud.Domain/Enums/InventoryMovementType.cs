namespace ROCloud.Domain.Enums;

/// <summary>Type of stock movement. DB: inventory_movements.movement_type.</summary>
public enum InventoryMovementType
{
    Issue,
    Return,
    Damage,
    Restock,
    Adjustment
}
