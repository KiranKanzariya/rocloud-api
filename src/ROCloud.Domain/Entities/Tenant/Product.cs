using ROCloud.Domain.Entities.Common;
using ROCloud.Domain.Enums;

namespace ROCloud.Domain.Entities.Tenant;

/// <summary>A sellable product (bottle/jar size). DB table: products.</summary>
public class Product : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public BottleSize BottleSize { get; set; }
    public decimal DefaultRate { get; set; }
    public string Unit { get; set; } = "bottle";
    /// <summary>HSN (goods) / SAC (services) code for GST tax invoices. Null → falls back to packaged-water HSN 2201.</summary>
    public string? Hsn { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
    public ICollection<CustomerSubscription> Subscriptions { get; set; } = new List<CustomerSubscription>();
}
