using ROCloud.Domain.Entities.Common;

namespace ROCloud.Domain.Entities.Tenant;

/// <summary>
/// A per-tenant message template for a (templateCode, languageCode, channel) tuple.
/// DB table: notification_templates. tenant_id is nullable (NULL rows are system defaults),
/// so this entity is NOT an ITenantEntity — handlers scope by tenant explicitly. The table has
/// updated_at but no is_deleted column.
/// </summary>
public class NotificationTemplate : BaseEntity
{
    public Guid? TenantId { get; set; }
    public string TemplateCode { get; set; } = string.Empty;
    public string LanguageCode { get; set; } = "en";
    public string Channel { get; set; } = "Email"; // Email | SMS | WhatsApp
    public string? Subject { get; set; }
    public string Body { get; set; } = string.Empty;
}
