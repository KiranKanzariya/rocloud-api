namespace ROCloud.Domain.Enums;

/// <summary>
/// Fulfilment method. DB: customers.delivery_mode (allows Both),
/// orders.delivery_mode (HomeDelivery/PlantPickup only).
/// </summary>
public enum DeliveryMode
{
    HomeDelivery,
    PlantPickup,
    Both
}
