using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using ROCloud.Application.Common.Interfaces;

namespace ROCloud.Infrastructure.Persistence;

/// <summary>
/// Appends audit rows with a raw parameterised INSERT on its own connection (guide §10.14),
/// so audit writes never flush the request's tracked changes and only need INSERT privilege
/// on the append-only table. Failures are logged, never thrown — auditing must not break the
/// request it is recording.
/// </summary>
public class AuditLogWriter : IAuditLogWriter
{
    private const string Sql = @"
        INSERT INTO audit_logs
            (id, tenant_id, user_id, module, action, entity_name, entity_id,
             new_values, ip_address, user_agent, status_code, created_at)
        VALUES
            (@id, @tenant_id, @user_id, @module, @action, @entity_name, @entity_id,
             @new_values, @ip_address, @user_agent, @status_code, @created_at)";

    private readonly string _connStr;
    private readonly ILogger<AuditLogWriter> _logger;

    public AuditLogWriter(IConfiguration config, ILogger<AuditLogWriter> logger)
    {
        _connStr = config.GetConnectionString("Default")
                   ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured");
        _logger = logger;
    }

    public async Task WriteAsync(AuditEntry entry, CancellationToken ct = default)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connStr);
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(Sql, conn);

            cmd.Parameters.AddWithValue("@id", Guid.NewGuid());
            cmd.Parameters.AddWithValue("@tenant_id", (object?)entry.TenantId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@user_id", (object?)entry.UserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@module", entry.Module);
            cmd.Parameters.AddWithValue("@action", entry.Action);
            cmd.Parameters.AddWithValue("@entity_name", (object?)entry.EntityName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@entity_id", (object?)entry.EntityId ?? DBNull.Value);
            cmd.Parameters.Add(new NpgsqlParameter("@new_values", NpgsqlDbType.Jsonb)
            {
                Value = (object?)entry.NewValues ?? DBNull.Value
            });
            cmd.Parameters.AddWithValue("@ip_address", (object?)entry.IpAddress ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@user_agent", (object?)entry.UserAgent ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@status_code", (object?)entry.StatusCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@created_at", DateTime.UtcNow);

            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to write audit log for {Module}/{Action}", entry.Module, entry.Action);
        }
    }
}
