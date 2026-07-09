using ROCloud.Domain.Entities.Common;

namespace ROCloud.Domain.Entities.Tenant;

/// <summary>
/// An append-only audit record. DB table: audit_logs (range-partitioned by created_at,
/// composite PK (id, created_at)). tenant_id is nullable, so this entity is NOT an
/// ITenantEntity (no automatic tenant query filter). The table has no
/// updated_at/is_deleted columns — Phase 3 ignores those BaseEntity members.
/// </summary>
public class AuditLog : BaseEntity
{
    public Guid? TenantId { get; set; }
    public Guid? UserId { get; set; }
    public string Module { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? EntityName { get; set; }
    public Guid? EntityId { get; set; }

    /// <summary>JSONB column (raw JSON). Mapped to jsonb in Phase 3.</summary>
    public string? OldValues { get; set; }

    /// <summary>JSONB column (raw JSON). Mapped to jsonb in Phase 3.</summary>
    public string? NewValues { get; set; }

    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }

    /// <summary>HTTP response status code of the audited request (200/201 = success, 4xx/5xx = failed).</summary>
    public int? StatusCode { get; set; }
}
