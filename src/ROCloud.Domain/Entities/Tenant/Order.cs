using ROCloud.Domain.Entities.Common;
using ROCloud.Domain.Enums;

namespace ROCloud.Domain.Entities.Tenant;

/// <summary>A customer order (1:M order items, 1:1 delivery). DB table: orders.</summary>
public class Order : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid? DeliveryBoyId { get; set; }
    public Guid? AreaId { get; set; }
    public DateOnly OrderDate { get; set; }
    public OrderType OrderType { get; set; } = OrderType.Regular;
    public DeliveryMode DeliveryMode { get; set; } = DeliveryMode.HomeDelivery;
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public string? Notes { get; set; }
    public Guid? CreatedBy { get; set; }

    // Navigation
    public Customer? Customer { get; set; }
    public Area? Area { get; set; }
    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
    public Delivery? Delivery { get; set; }
}
