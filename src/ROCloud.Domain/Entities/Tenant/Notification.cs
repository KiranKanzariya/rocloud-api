using ROCloud.Domain.Entities.Common;

namespace ROCloud.Domain.Entities.Tenant;

/// <summary>
/// An in-app notification for a tenant user (the owner's bell feed, guide §24). Rows are generated
/// from actionable tenant state (overdue invoices, pending orders, AMC due, open service requests)
/// and kept idempotent per (tenant, user, type): the row is updated in place as the underlying
/// count changes and re-flagged unread on change. DB table: notifications (no is_deleted column).
/// </summary>
public class Notification : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }

    /// <summary>Stable category — InvoicesOverdue | OrdersPending | AmcDue | ServiceOpen.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>English fallback text (the portal prefers a translated label keyed off Type + count).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Portal route to open, e.g. /invoices.</summary>
    public string? Link { get; set; }

    /// <summary>Change-detection token (the current count). A different value re-alerts as unread.</summary>
    public string ReferenceKey { get; set; } = string.Empty;

    public bool IsRead { get; set; }
}
