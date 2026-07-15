namespace ROCloud.Application.Features.Deliveries.Dtos;

/// <summary>A row on a delivery list / kanban card.</summary>
public sealed record DeliveryListItemDto(
    Guid Id,
    Guid OrderId,
    string Status,
    // Fulfilment method of the order (HomeDelivery/PlantPickup) — lets the UI offer a direct
    // "Mark as Picked Up" action for pickups instead of the InTransit → Delivered flow.
    string? DeliveryMode,
    DateOnly ScheduledDate,
    DateTime? DeliveredAt,
    Guid CustomerId,
    string CustomerName,
    string? CustomerMobile,
    string? AddressLine,
    string? AreaName,
    Guid? DeliveryBoyId,
    string? DeliveryBoyName,
    int? JarsDelivered,
    int? JarsReturned,
    decimal? CollectedAmount,
    string? PaymentMethod,
    string? ProofImageUrl,
    decimal? Latitude,
    decimal? Longitude,
    string? Notes,
    // The order's lines (product + ordered qty) so a card can show what/how many to deliver.
    IReadOnlyList<DeliveryLineDto> Items,
    // Per-product jars actually handed over / brought back — shown on a Delivered card so the board
    // reveals WHICH jar went out and came back, not just the total. Empty until delivered (and for
    // older single-count deliveries, which have no per-item rows).
    IReadOnlyList<DeliveredLineDto> DeliveredLines)
{
    // Off-order empties returned on this stop (product not on the order). Not part of the SQL
    // projection — it needs an in-memory group — so the board handler fills it after materialising.
    public IReadOnlyList<DeliveredOtherReturnDto> OtherReturns { get; set; } = [];
}

/// <summary>An off-order empty returned on a delivery card: product name + how many.</summary>
public sealed record DeliveredOtherReturnDto(string ProductName, int Quantity);

/// <summary>An ordered line on a delivery card: product name + ordered jar quantity.</summary>
public sealed record DeliveryLineDto(string ProductName, int Quantity);

/// <summary>A completed line on a delivery card: product name + jars out / back.</summary>
public sealed record DeliveredLineDto(string ProductName, int JarsDelivered, int JarsReturned);

/// <summary>What was actually recorded at a completed stop — for the read-only delivery summary.</summary>
public sealed record DeliveryDetailDto(
    Guid Id,
    Guid OrderId,
    string CustomerName,
    string Status,
    DateTime? DeliveredAt,
    decimal? CollectedAmount,
    string? PaymentMethod,
    string? ProofImageUrl,
    string? Notes,
    int? JarsDelivered,
    int? JarsReturned,
    IReadOnlyList<DeliveryItemDetailDto> Items,
    IReadOnlyList<DeliveryOtherReturnDto> OtherReturns);

public sealed record DeliveryItemDetailDto(string ProductName, string BottleSize, int JarsDelivered, int JarsReturned);

/// <summary>An empty returned for a product not on the order (recorded as a customer Return movement).</summary>
public sealed record DeliveryOtherReturnDto(string ProductName, string BottleSize, int Quantity);

/// <summary>Kanban grouping for the delivery board UI.</summary>
public sealed record DeliveryBoardDto(
    IReadOnlyList<DeliveryListItemDto> Pending,
    IReadOnlyList<DeliveryListItemDto> InTransit,
    IReadOnlyList<DeliveryListItemDto> Delivered,
    IReadOnlyList<DeliveryListItemDto> Failed,
    // Plant-pickup stops, kept out of the route columns above (no delivery boy / van load) — the
    // customer collects at the plant. Shown as a separate section on the board.
    IReadOnlyList<DeliveryListItemDto> Pickups,
    // Item-wise jar totals still to be delivered (Pending + In transit), so the boy/owner can
    // load the vehicle at a glance. Respects the board's filters (date / area / delivery boy).
    IReadOnlyList<BoardItemTotalDto> ToDeliver);

/// <summary>A product's total jars still to be delivered across the board's outstanding stops.</summary>
public sealed record BoardItemTotalDto(string ProductName, string BottleSize, int Quantity);

/// <summary>Per-delivery-boy progress row for the dashboard summary.</summary>
public sealed record DeliverySummaryRowDto(
    Guid? DeliveryBoyId,
    string DeliveryBoyName,
    int Total,
    int Pending,
    int InTransit,
    int Delivered,
    int Failed,
    double CompletedPercentage);

/// <summary>Filter options for the delivery list/board.</summary>
public sealed record DeliveryFilterDto
{
    public DateOnly? Date { get; init; }
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
    public Guid? AreaId { get; init; }
    public Guid? DeliveryBoyId { get; init; }
    public string? Status { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}

/// <summary>Per-order-item jars handed over / brought back on a delivery.</summary>
public sealed record DeliveryItemInputDto(Guid OrderItemId, int JarsDelivered, int JarsReturned);

/// <summary>
/// Empties brought back for a product that is NOT on this order (e.g. an 18L order where the customer
/// also returns a 20L). Recorded as customer-scoped Return movements against that product's balance.
/// </summary>
public sealed record OtherReturnInputDto(Guid ProductId, int Quantity);

/// <summary>Payload for a delivery boy updating a stop's status from the mobile route.</summary>
public sealed record UpdateDeliveryStatusDto(
    string Status,
    int? JarsDelivered,
    int? JarsReturned,
    decimal? CollectedAmount,
    string? PaymentMethod,
    string? ProofImageUrl,
    decimal? Latitude,
    decimal? Longitude,
    string? Notes,
    IReadOnlyList<DeliveryItemInputDto>? Items = null,
    IReadOnlyList<OtherReturnInputDto>? OtherReturns = null);
