namespace ROCloud.Application.Common.Interfaces;

/// <summary>
/// Bottle-float tracking used by the Orders/Deliveries modules and the Inventory feature.
/// Every method get-or-creates the per-product <c>Inventory</c> row, updates its counters,
/// and appends an <c>InventoryMovement</c> — but does NOT call SaveChanges, so the caller
/// owns the unit of work (one transaction with the surrounding operation).
/// </summary>
public interface IInventoryService
{
    /// <summary>Jars issued to a customer: issuedStock += quantity (+ Issue movement).</summary>
    Task RecordIssueAsync(Guid productId, int quantity, Guid? orderId, Guid? customerId, CancellationToken ct = default);

    /// <summary>Jars returned from a customer: issuedStock -= quantity, returnedStock += quantity (+ Return movement).</summary>
    Task RecordReturnAsync(Guid productId, int quantity, Guid? orderId, Guid? customerId, CancellationToken ct = default);

    /// <summary>Jars written off: damagedStock += quantity (+ Damage movement).</summary>
    Task RecordDamageAsync(Guid productId, int quantity, string? notes, CancellationToken ct = default);
}
