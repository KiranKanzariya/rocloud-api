using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace ROCloud.API.HealthChecks;

/// <summary>
/// Readiness check that verifies the PostgreSQL connection with a lightweight round-trip
/// (guide §16). Tagged "ready" so it backs /health/ready but not /health/live.
/// </summary>
public class DatabaseHealthCheck : IHealthCheck
{
    private readonly string _connStr;

    public DatabaseHealthCheck(IConfiguration config)
        => _connStr = config.GetConnectionString("Default")
                      ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured");

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connStr);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = new NpgsqlCommand("SELECT 1", conn);
            await cmd.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy("Database reachable");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database unreachable", ex);
        }
    }
}
