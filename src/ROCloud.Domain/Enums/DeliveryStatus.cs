namespace ROCloud.Domain.Enums;

/// <summary>Delivery workflow state. DB: deliveries.status.</summary>
public enum DeliveryStatus
{
    Pending,
    InTransit,
    Delivered,
    Failed,
    Skipped
}
