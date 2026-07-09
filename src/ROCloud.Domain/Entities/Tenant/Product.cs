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
    public bool IsActive { get; set; } = true;

    // Navigation
    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
    public ICollection<CustomerSubscription> Subscriptions { get; set; } = new List<CustomerSubscription>();
}
