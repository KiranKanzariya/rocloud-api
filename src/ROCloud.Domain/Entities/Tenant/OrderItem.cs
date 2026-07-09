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
    public decimal UnitRate { get; set; }

    /// <summary>Generated in the DB as quantity * unit_rate (read-only at runtime).</summary>
    public decimal TotalAmount { get; set; }

    // Navigation
    public Order? Order { get; set; }
    public Product? Product { get; set; }
}
