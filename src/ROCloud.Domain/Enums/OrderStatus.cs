namespace ROCloud.Domain.Enums;

/// <summary>Order workflow state. DB: orders.status.</summary>
public enum OrderStatus
{
    Pending,
    Confirmed,
    InTransit,
    Delivered,
    Cancelled,
    Returned,
    /// <summary>The delivery was attempted and failed — the order was not delivered. Set when its
    /// delivery is marked Failed, so the order stops reading "InTransit" everywhere it's shown.</summary>
    Failed
}
