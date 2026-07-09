namespace ROCloud.Domain.Entities.Common;

/// <summary>
/// Base type for every persisted entity. Provides the surrogate key,
/// audit timestamps, and the soft-delete flag.
/// </summary>
/// <remarks>
/// Not every table carries <c>updated_at</c> / <c>is_deleted</c> columns
/// (e.g. order_items, deliveries, inventory, inventory_movements, payments,
/// audit_logs). For those, Phase 3 EF configuration ignores the unmapped
/// members and adjusts the tenant query filter accordingly.
/// </remarks>
public abstract class BaseEntity
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
}
