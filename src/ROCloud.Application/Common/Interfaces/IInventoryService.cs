namespace ROCloud.Application.Common.Interfaces;

/// <summary>
/// Bottle-float tracking used by the Orders/Deliveries modules and the Inventory feature.
/// Every method get-or-creates the per-product <c>Inventory</c> row, updates its counters,
/// and appends an <c>InventoryMovement</c> — but does NOT call SaveChanges, so the caller
/// owns the unit of work (one transaction with the surrounding operation).
///
/// <para>
/// <c>occurredOn</c> is the business day the jars actually moved, used when a stop is closed after
/// its day (a late or backdated delivery). The movement is then stamped midday of that day in the
/// app timezone so period reports — "jars delivered this month" — count it in the month it happened
/// rather than the month it was typed in. Omitted/null → stamped now, the normal same-day case.
/// </para>
/// </summary>
public interface IInventoryService
{
    /// <summary>Jars issued to a customer: issuedStock += quantity (+ Issue movement).</summary>
    Task RecordIssueAsync(
        Guid productId, int quantity, Guid? orderId, Guid? customerId,
        DateOnly? occurredOn = null, CancellationToken ct = default);

    /// <summary>Jars returned from a customer: issuedStock -= quantity, returnedStock += quantity (+ Return movement).</summary>
    Task RecordReturnAsync(
        Guid productId, int quantity, Guid? orderId, Guid? customerId,
        DateOnly? occurredOn = null, CancellationToken ct = default);

    /// <summary>Jars written off: damagedStock += quantity (+ Damage movement).</summary>
    Task RecordDamageAsync(Guid productId, int quantity, string? notes, CancellationToken ct = default);
}
