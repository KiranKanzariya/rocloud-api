namespace ROCloud.Domain.Enums;

/// <summary>Order workflow state. DB: orders.status.</summary>
public enum OrderStatus
{
    Pending,
    Confirmed,
    InTransit,
    Delivered,
    Cancelled,
    Returned
}
