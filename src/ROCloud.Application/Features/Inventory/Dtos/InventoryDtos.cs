namespace ROCloud.Application.Features.Inventory.Dtos;

/// <summary>Per-product stock summary. AvailableStock = total - issued - damaged.</summary>
public sealed record InventorySummaryDto(
    Guid ProductId,
    string ProductName,
    string BottleSize,
    int TotalStock,
    int IssuedStock,
    int ReturnedStock,
    int DamagedStock,
    int AvailableStock,
    DateTime? LastUpdated);

/// <summary>A single row in the movement ledger.</summary>
public sealed record InventoryMovementDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string MovementType,
    int Quantity,
    Guid? OrderId,
    Guid? CustomerId,
    string? CustomerName,
    Guid? PerformedBy,
    string? Notes,
    DateTime CreatedAt);

/// <summary>Filter/paging for the movement log.</summary>
public sealed record InventoryMovementFilterDto
{
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
    public Guid? ProductId { get; init; }
    public Guid? CustomerId { get; init; }
    public string? MovementType { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}
