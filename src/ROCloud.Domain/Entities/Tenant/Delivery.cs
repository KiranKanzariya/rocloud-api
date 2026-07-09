using ROCloud.Domain.Entities.Common;
using ROCloud.Domain.Enums;

namespace ROCloud.Domain.Entities.Tenant;

/// <summary>
/// The delivery for an order. DB table: deliveries. The table has no is_deleted
/// column — Phase 3 ignores IsDeleted and omits the soft-delete clause from this
/// entity's tenant query filter.
/// </summary>
public class Delivery : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid OrderId { get; set; }
    public Guid? DeliveryBoyId { get; set; }
    public DateOnly ScheduledDate { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DeliveryStatus Status { get; set; } = DeliveryStatus.Pending;
    public int? JarsDelivered { get; set; } = 0;
    public int? JarsReturned { get; set; } = 0;
    public decimal? CollectedAmount { get; set; } = 0;
    public PaymentMethod? PaymentMethod { get; set; }
    public string? ProofImageUrl { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public string? Notes { get; set; }

    // Navigation
    public Order? Order { get; set; }
    public ICollection<DeliveryItem> Items { get; set; } = new List<DeliveryItem>();
}
