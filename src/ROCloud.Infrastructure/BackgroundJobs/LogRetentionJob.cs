using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ROCloud.Infrastructure.BackgroundJobs;

/// <summary>
/// Daily cleanup of the Serilog "logs" table (technical/diagnostic logs), deleting rows older than
/// Logs:RetentionDays (default 30; 0 = keep forever). The table is created and owned by the app role
/// (Serilog's needAutoCreateTable), so a plain DELETE works — no elevated role needed. Never throws:
/// a cleanup failure must not crash the host.
/// </summary>
public class LogRetentionJob
{
    private readonly string _connStr;
    private readonly int _retentionDays;
    private readonly ILogger<LogRetentionJob> _logger;

    public LogRetentionJob(IConfiguration config, ILogger<LogRetentionJob> logger)
    {
        _connStr = config.GetConnectionString("Default")
                   ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured");
        _retentionDays = int.TryParse(config["Logs:RetentionDays"], out var d) ? d : 30;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        if (_retentionDays <= 0)
        {
            _logger.LogInformation("LogRetention: retention disabled (keep forever).");
            return;
        }

        try
        {
            await using var conn = new NpgsqlConnection(_connStr);
            await conn.OpenAsync(ct);

            // logs.timestamp is "timestamp without time zone" in server-local time (what Serilog wrote);
            // compare against LOCALTIMESTAMP. _retentionDays is a validated int from config (not user input).
            await using var cmd = new NpgsqlCommand(
                $"DELETE FROM logs WHERE timestamp < LOCALTIMESTAMP - make_interval(days => {_retentionDays});", conn);
            var deleted = await cmd.ExecuteNonQueryAsync(ct);

            _logger.LogInformation("LogRetention: deleted {Count} log row(s) older than {Days} day(s).", deleted, _retentionDays);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            // No "logs" table yet (e.g. the sink only writes outside Development) — nothing to do.
            _logger.LogInformation("LogRetention: 'logs' table not present; nothing to prune.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "LogRetention: failed to prune logs.");
        }
    }
}
