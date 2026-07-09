using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using ROCloud.Application.Common.Interfaces;

namespace ROCloud.Infrastructure.BackgroundJobs;

/// <summary>
/// Monthly pruning of the activity log: drops audit_logs partitions older than the configured
/// RetentionMonths (0 = keep forever). DROP requires OWNERSHIP of audit_logs, which the app role does
/// not have in production (the table is postgres-owned). Configure a privileged connection via
/// "Audit:RetentionConnectionString" for this job; otherwise it falls back to the default connection
/// and logs a warning if it lacks privilege (it never throws — pruning failures must not crash the host).
/// </summary>
public partial class AuditLogRetentionJob
{
    private readonly string _connStr;
    private readonly IAuditSettingsProvider _settings;
    private readonly ILogger<AuditLogRetentionJob> _logger;

    public AuditLogRetentionJob(IConfiguration config, IAuditSettingsProvider settings, ILogger<AuditLogRetentionJob> logger)
    {
        _connStr = config["Audit:RetentionConnectionString"]
                   ?? config.GetConnectionString("Default")
                   ?? throw new InvalidOperationException("No connection string for audit retention.");
        _settings = settings;
        _logger = logger;
    }

    [GeneratedRegex(@"^audit_logs_(\d{4})_(\d{2})$")]
    private static partial Regex PartitionName();

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var settings = await _settings.GetAsync(ct);
        if (settings.RetentionMonths <= 0)
        {
            _logger.LogInformation("AuditLogRetention: retention disabled (keep forever).");
            return;
        }

        var now = DateTime.UtcNow;
        var cutoff = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc)
            .AddMonths(-settings.RetentionMonths);

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync(ct);

        // List the partitions of audit_logs.
        var partitions = new List<string>();
        const string listSql = @"
            SELECT c.relname
            FROM pg_inherits i
            JOIN pg_class c ON c.oid = i.inhrelid
            JOIN pg_class p ON p.oid = i.inhparent
            WHERE p.relname = 'audit_logs';";
        await using (var listCmd = new NpgsqlCommand(listSql, conn))
        await using (var reader = await listCmd.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
                partitions.Add(reader.GetString(0));

        var dropped = 0;
        foreach (var name in partitions)
        {
            var m = PartitionName().Match(name);
            if (!m.Success) continue;

            var monthStart = new DateTime(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), 1, 0, 0, 0, DateTimeKind.Utc);
            if (monthStart >= cutoff) continue;

            try
            {
                // Name comes from the system catalog and matches our fixed pattern — safe to interpolate.
                await using var dropCmd = new NpgsqlCommand($"DROP TABLE IF EXISTS \"{name}\";", conn);
                await dropCmd.ExecuteNonQueryAsync(ct);
                dropped++;
                _logger.LogInformation("AuditLogRetention: dropped partition {Partition}", name);
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InsufficientPrivilege)
            {
                _logger.LogWarning(
                    "AuditLogRetention: insufficient privilege to drop {Partition}. Configure Audit:RetentionConnectionString with an owner role.", name);
                return; // no point trying the rest with the same role
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "AuditLogRetention: failed to drop {Partition}", name);
            }
        }

        _logger.LogInformation(
            "AuditLogRetention: kept partitions on/after {Cutoff:yyyy-MM}, dropped {Dropped}.", cutoff, dropped);
    }
}
