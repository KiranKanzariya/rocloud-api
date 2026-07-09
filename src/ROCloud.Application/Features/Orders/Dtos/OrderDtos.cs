namespace ROCloud.Application.Features.Orders.Dtos;

/// <summary>Lightweight row for the orders list/grid.</summary>
public sealed record OrderListItemDto(
    Guid Id,
    DateOnly OrderDate,
    string CustomerName,
    string? CustomerMobile,
    string? AreaName,
    string? DeliveryBoyName,
    string OrderType,
    string DeliveryMode,
    string Status,
    int ItemCount,
    decimal TotalAmount,
    string? DeliveryStatus,
    decimal AmountPaid,
    string PaymentStatus,
    // When the order was recorded — lets the UI tell apart multiple orders placed on the same OrderDate.
    DateTime CreatedAt);

/// <summary>Full order for the detail view, with items and the linked delivery.</summary>
public sealed record OrderDto(
    Guid Id,
    DateOnly OrderDate,
    Guid CustomerId,
    string CustomerName,
    string? CustomerMobile,
    Guid? AreaId,
    string? AreaName,
    Guid? DeliveryBoyId,
    string? DeliveryBoyName,
    string OrderType,
    string DeliveryMode,
    string Status,
    string? Notes,
    decimal TotalAmount,
    decimal AmountPaid,
    string PaymentStatus,
    DateTime CreatedAt,
    IReadOnlyList<OrderItemDto> Items,
    OrderDeliveryDto? Delivery);

public sealed record OrderItemDto(
    Guid Id, Guid ProductId, string ProductName, int Quantity, decimal UnitRate, decimal TotalAmount);

/// <summary>The 1:1 delivery summary shown inside an order.</summary>
public sealed record OrderDeliveryDto(
    Guid Id,
    string Status,
    DateOnly ScheduledDate,
    DateTime? DeliveredAt,
    int? JarsDelivered,
    int? JarsReturned,
    decimal? CollectedAmount,
    string? PaymentMethod,
    string? ProofImageUrl);

/// <summary>A line item on a create-order request (enum-free; product drives the rate).</summary>
public sealed record CreateOrderItemDto(Guid ProductId, int Quantity, decimal? UnitRate);

/// <summary>Result of a bulk subscription run.</summary>
public sealed record BulkCreateResultDto(int OrdersCreated, int SubscriptionsConsidered, int Skipped);

/// <summary>A product + quantity line, used to show what's inside an upcoming booking.</summary>
public sealed record OrderLineSummaryDto(string ProductName, int Quantity);

/// <summary>
/// A future-dated booking for the Upcoming tab, including its line items so the owner sees what
/// (and how much) each customer has booked, not just a count.
/// </summary>
public sealed record UpcomingOrderDto(
    Guid Id,
    DateOnly OrderDate,
    string CustomerName,
    string? CustomerMobile,
    string? AreaName,
    string? DeliveryBoyName,
    string OrderType,
    string DeliveryMode,
    string Status,
    int ItemCount,
    int TotalQuantity,
    decimal TotalAmount,
    IReadOnlyList<OrderLineSummaryDto> Items,
    DateTime CreatedAt);

/// <summary>
/// One product's aggregate demand on a single future date — the total quantity across all
/// upcoming orders, so the plant knows how much to prepare. See <see cref="ProductionPlanDayDto"/>.
/// </summary>
public sealed record ProductionPlanLineDto(
    Guid ProductId, string ProductName, int TotalQuantity, int OrderCount);

/// <summary>Everything the plant must prepare for one future date, plus the customer bookings behind it.</summary>
public sealed record ProductionPlanDayDto(
    DateOnly Date,
    int TotalUnits,
    int OrderCount,
    IReadOnlyList<ProductionPlanLineDto> Lines,
    IReadOnlyList<ProductionPlanBookingDto> Bookings);

/// <summary>A single customer booking contributing to a production-plan day (for the drill-down).</summary>
public sealed record ProductionPlanBookingDto(
    Guid OrderId, string CustomerName, string? AreaName, string OrderType, int TotalQuantity,
    IReadOnlyList<OrderLineSummaryDto> Items);

/// <summary>Filter/paging/sort options for the orders list.</summary>
public sealed record OrderFilterDto
{
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
    public Guid? AreaId { get; init; }
    public Guid? CustomerId { get; init; }
    public Guid? DeliveryBoyId { get; init; }
    public string? Status { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
    public string? SortBy { get; init; }
    public string? SortDir { get; init; }
}
