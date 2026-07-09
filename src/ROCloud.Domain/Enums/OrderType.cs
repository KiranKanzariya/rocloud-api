namespace ROCloud.Domain.Enums;

/// <summary>Classification of an order. DB: orders.order_type.</summary>
public enum OrderType
{
    Regular,
    Urgent,
    Subscription,
    BulkReturn,

    /// <summary>
    /// A future-dated one-time booking (event/program). The customer tells the owner in advance
    /// "I need N bottles on this day". Created with a future OrderDate; surfaces on the day-scoped
    /// delivery board when its date arrives, and in the Upcoming Orders / Production Plan views before then.
    /// </summary>
    Advance
}
