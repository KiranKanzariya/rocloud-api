namespace ROCloud.Application.Common.Interfaces;

/// <summary>A single audit record to append (guide §10.14).</summary>
public sealed record AuditEntry(
    Guid? TenantId,
    Guid? UserId,
    string Module,
    string Action,
    string? EntityName,
    Guid? EntityId,
    string? NewValues,
    string? IpAddress,
    string? UserAgent,
    int? StatusCode);

/// <summary>
/// Appends to the tamper-evident, partitioned audit_logs table (guide §10.14). Writes are
/// isolated from the request's EF change-tracker and the table is append-only at the DB level
/// (see scripts/audit-permissions.sql).
/// </summary>
public interface IAuditLogWriter
{
    Task WriteAsync(AuditEntry entry, CancellationToken ct = default);
}
