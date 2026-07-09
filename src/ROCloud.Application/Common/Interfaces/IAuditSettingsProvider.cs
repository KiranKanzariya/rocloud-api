namespace ROCloud.Application.Common.Interfaces;

/// <summary>
/// Effective audit configuration used by the hot-path AuditMiddleware and the retention job.
/// A plain snapshot so it can be cached. Defaults mirror the system's original hardcoded behaviour.
/// </summary>
public sealed class AuditSettingsSnapshot
{
    public bool Enabled { get; set; } = true;
    public bool CaptureRequestBody { get; set; } = true;
    public int MaxRequestBodyBytes { get; set; } = 102400;
    public string[] Methods { get; set; } = ["POST", "PUT", "PATCH", "DELETE"];
    public string[] SensitivePathPrefixes { get; set; } = ["/api/auth", "/api/payments"];
    public string[] ExcludeModules { get; set; } = [];
    public string[] AuditReadsForModules { get; set; } = [];
    public string[] AdditionalRedactKeys { get; set; } = [];
    public int RetentionMonths { get; set; }

    public static AuditSettingsSnapshot Defaults() => new();
}

/// <summary>
/// Supplies the current audit settings to the middleware/jobs, cached so the per-request middleware
/// never queries the database. Fail-safe: returns <see cref="AuditSettingsSnapshot.Defaults"/> if the
/// settings cannot be read, so auditing continues rather than silently stopping.
/// </summary>
public interface IAuditSettingsProvider
{
    Task<AuditSettingsSnapshot> GetAsync(CancellationToken ct = default);

    /// <summary>Drop the cached snapshot so the next read reflects a just-saved change.</summary>
    Task InvalidateAsync(CancellationToken ct = default);
}
