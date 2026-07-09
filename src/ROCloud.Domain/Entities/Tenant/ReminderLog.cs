using ROCloud.Domain.Entities.Common;

namespace ROCloud.Domain.Entities.Tenant;

/// <summary>Known reminder types recorded in <see cref="ReminderLog"/> (matches the notification template codes).</summary>
public static class ReminderTypes
{
    public const string Payment = "payment";
    public const string Amc = "amc";
    public const string AdvanceOrder = "advance_order";

    /// <summary>Platform → owner dunning notice; SubjectId is the tenant id (not a customer).</summary>
    public const string SubscriptionExpiry = "subscription_expiry";
}

/// <summary>
/// One row per reminder actually sent to a customer, so the recurring reminder jobs can throttle by
/// cadence — they skip a subject that was already reminded within the configured interval instead of
/// re-sending on every run. Insert-only (no updated_at / is_deleted). <see cref="BaseEntity.CreatedAt"/>
/// is the sent time. DB table: reminder_log. Tenant-scoped (app-level filter, like notifications).
/// </summary>
public class ReminderLog : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>What kind of reminder — see <see cref="ReminderTypes"/> (payment | amc | advance_order).</summary>
    public string ReminderType { get; set; } = string.Empty;

    /// <summary>The entity the cadence is throttled on: customer id (payment) or the service-request / order id.</summary>
    public Guid SubjectId { get; set; }

    /// <summary>The customer reminded, for audit/reporting (may equal SubjectId for payment reminders).</summary>
    public Guid? CustomerId { get; set; }

    /// <summary>The channel used — 'WhatsApp' | 'Email'.</summary>
    public string Channel { get; set; } = string.Empty;
}
