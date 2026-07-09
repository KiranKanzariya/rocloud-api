using ROCloud.Domain.Entities.Common;
using ROCloud.Domain.Enums;

namespace ROCloud.Domain.Entities.Tenant;

/// <summary>
/// An individual stock movement (issue/return/damage/restock/adjustment).
/// DB table: inventory_movements. The table has no updated_at/is_deleted columns —
/// Phase 3 ignores those BaseEntity members.
/// </summary>
public class InventoryMovement : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? OrderId { get; set; }
    public Guid? CustomerId { get; set; }
    public InventoryMovementType MovementType { get; set; }
    public int Quantity { get; set; }
    public Guid? PerformedBy { get; set; }
    public string? Notes { get; set; }

    // Navigation
    public Product? Product { get; set; }
}
