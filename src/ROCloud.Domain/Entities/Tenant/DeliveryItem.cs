using ROCloud.Domain.Entities.Common;

namespace ROCloud.Domain.Entities.Tenant;

/// <summary>
/// Per-product jars delivered/returned for one delivery, so a multi-item order can record how many
/// of each product were handed over and how many empties came back (guide §9). DB table: delivery_items.
/// </summary>
public class DeliveryItem : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid DeliveryId { get; set; }
    public Guid OrderItemId { get; set; }
    public Guid ProductId { get; set; }
    public int JarsDelivered { get; set; }
    public int JarsReturned { get; set; }

    // Navigation
    public Delivery? Delivery { get; set; }
}
