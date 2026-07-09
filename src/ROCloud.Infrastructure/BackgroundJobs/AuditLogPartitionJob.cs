using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ROCloud.Infrastructure.BackgroundJobs;

/// <summary>
/// Monthly (25th, 00:00) creation of next month's audit_logs range partition, so writes never
/// hit a missing partition. The bounds are computed from the clock (no user input) and the
/// partition is created IF NOT EXISTS, so re-runs are safe. Requires a role with CREATE on the
/// schema (run as the migration/owner role in production).
/// </summary>
public class AuditLogPartitionJob
{
    private readonly string _connStr;
    private readonly ILogger<AuditLogPartitionJob> _logger;

    public AuditLogPartitionJob(IConfiguration config, ILogger<AuditLogPartitionJob> logger)
    {
        _connStr = config.GetConnectionString("Default")
                   ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured");
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var firstOfNextMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc)
            .AddMonths(1);
        var from = firstOfNextMonth;
        var to = firstOfNextMonth.AddMonths(1);

        // Names/bounds are derived from the clock — never user input — so this DDL is safe.
        var partitionName = $"audit_logs_{from:yyyy}_{from:MM}";
        var sql =
            $"CREATE TABLE IF NOT EXISTS {partitionName} PARTITION OF audit_logs " +
            $"FOR VALUES FROM ('{from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}') " +
            $"TO ('{to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}');";

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogInformation("AuditLogPartition: ensured partition {Partition}", partitionName);
    }
}
