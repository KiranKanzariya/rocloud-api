using ROCloud.Domain.Entities.Common;

namespace ROCloud.Domain.Entities.Tenant;

/// <summary>
/// Per-product stock totals for a tenant (unique per tenant+product). DB table: inventory.
/// The table has no created_at/updated_at/is_deleted columns — Phase 3 ignores those
/// BaseEntity members. last_updated is its own column.
/// </summary>
public class Inventory : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ProductId { get; set; }
    public int TotalStock { get; set; }
    public int IssuedStock { get; set; }
    public int ReturnedStock { get; set; }
    public int DamagedStock { get; set; }
    public DateTime LastUpdated { get; set; }

    // Navigation
    public Product? Product { get; set; }
}
