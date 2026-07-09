using ROCloud.Domain.Entities.Common;

namespace ROCloud.Domain.Entities.Platform;

/// <summary>
/// Global, SuperAdmin-managed configuration for the activity log / audit middleware (guide §10.14).
/// A single row (enforced by a unique index) — these settings are platform-wide, not per-tenant.
/// DB table: audit_settings (no is_deleted column).
/// </summary>
public class AuditSettings : BaseEntity
{
    /// <summary>Master switch. When false, only the compliance floor (auth/payments mutations) is logged.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Whether to capture the (redacted) request body as new_values.</summary>
    public bool CaptureRequestBody { get; set; } = true;

    /// <summary>Bodies larger than this are not captured.</summary>
    public int MaxRequestBodyBytes { get; set; } = 102400;

    /// <summary>HTTP methods treated as audit-worthy mutations.</summary>
    public string[] Methods { get; set; } = ["POST", "PUT", "PATCH", "DELETE"];

    /// <summary>Path prefixes audited for ANY method (e.g. /api/auth, /api/payments).</summary>
    public string[] SensitivePathPrefixes { get; set; } = ["/api/auth", "/api/payments"];

    /// <summary>Modules (first path segment) to skip, e.g. "notifications".</summary>
    public string[] ExcludeModules { get; set; } = [];

    /// <summary>Modules whose GET (read) requests should also be audited.</summary>
    public string[] AuditReadsForModules { get; set; } = [];

    /// <summary>Extra JSON field names to mask, merged with the built-in redaction list.</summary>
    public string[] AdditionalRedactKeys { get; set; } = [];

    /// <summary>Drop audit partitions older than this many months. 0 = keep forever.</summary>
    public int RetentionMonths { get; set; }

    /// <summary>The platform user who last changed these settings.</summary>
    public Guid? UpdatedBy { get; set; }
}
