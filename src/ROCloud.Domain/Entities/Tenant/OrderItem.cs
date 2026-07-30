using ROCloud.Domain.Entities.Common;

namespace ROCloud.Domain.Entities.Tenant;

/// <summary>
/// A line item on an order. DB table: order_items. The table has no
/// created_at/updated_at/is_deleted columns — Phase 3 ignores those BaseEntity
/// members. TotalAmount is a STORED generated column (quantity * unit_rate).
/// </summary>
public class OrderItem : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid OrderId { get; set; }
    public Guid ProductId { get; set; }
    public int Quantity { get; set; } = 1;

    /// <summary>
    /// The quantity originally ordered, snapshotted when the line is created/edited. <see cref="Quantity"/>
    /// is rewritten to the jars actually handed over when the stop is marked Delivered (bill-what-was-
    /// delivered), so this preserves the plan for reporting/audit. Equals <see cref="Quantity"/> until a
    /// delivery differs from what was ordered.
    /// </summary>
    public int OrderedQuantity { get; set; } = 1;
    public decimal UnitRate { get; set; }

    /// <summary>Generated in the DB as quantity * unit_rate (read-only at runtime).</summary>
    public decimal TotalAmount { get; set; }

    // Navigation
    public Order? Order { get; set; }
    public Product? Product { get; set; }
}
