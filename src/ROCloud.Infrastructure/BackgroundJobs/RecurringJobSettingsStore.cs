using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ROCloud.Infrastructure.BackgroundJobs;

/// <summary>
/// Persists per-recurring-job overrides (cron + enabled) in the <c>recurring_job_settings</c> table,
/// via raw parameterised SQL on its own connection (like <see cref="Persistence.AuditLogWriter"/>).
/// All methods are resilient: if the table doesn't exist yet (migration not applied), reads return
/// empty and writes are no-ops + logged, so the API still boots and falls back to code defaults.
/// </summary>
public class RecurringJobSettingsStore
{
    public record Setting(string JobId, string Cron, bool Enabled);

    private readonly string _connStr;
    private readonly ILogger<RecurringJobSettingsStore> _logger;

    public RecurringJobSettingsStore(IConfiguration config, ILogger<RecurringJobSettingsStore> logger)
    {
        _connStr = config.GetConnectionString("Default")
                   ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured");
        _logger = logger;
    }

    public IReadOnlyDictionary<string, Setting> GetAll()
    {
        var result = new Dictionary<string, Setting>(StringComparer.Ordinal);
        try
        {
            using var conn = new NpgsqlConnection(_connStr);
            conn.Open();
            using var cmd = new NpgsqlCommand("SELECT job_id, cron, enabled FROM recurring_job_settings", conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result[reader.GetString(0)] = new Setting(reader.GetString(0), reader.GetString(1), reader.GetBoolean(2));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read recurring_job_settings (table missing?); falling back to code defaults.");
        }
        return result;
    }

    /// <summary>Insert a default row for a job the first time it's seen. No-op if it already exists.</summary>
    public void SeedIfMissing(string jobId, string cron, bool enabled = true)
        => Execute(
            "INSERT INTO recurring_job_settings (job_id, cron, enabled) VALUES (@id, @cron, @enabled) ON CONFLICT (job_id) DO NOTHING",
            jobId, cmd =>
            {
                cmd.Parameters.AddWithValue("@cron", cron);
                cmd.Parameters.AddWithValue("@enabled", enabled);
            });

    public bool SetEnabled(string jobId, bool enabled)
        => Execute(
            "UPDATE recurring_job_settings SET enabled = @enabled, updated_at = NOW() WHERE job_id = @id",
            jobId, cmd => cmd.Parameters.AddWithValue("@enabled", enabled));

    public bool SetCron(string jobId, string cron)
        => Execute(
            "UPDATE recurring_job_settings SET cron = @cron, updated_at = NOW() WHERE job_id = @id",
            jobId, cmd => cmd.Parameters.AddWithValue("@cron", cron));

    private bool Execute(string sql, string jobId, Action<NpgsqlCommand> addParams)
    {
        try
        {
            using var conn = new NpgsqlConnection(_connStr);
            conn.Open();
            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", jobId);
            addParams(cmd);
            return cmd.ExecuteNonQuery() > 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "recurring_job_settings write failed for {JobId} (table missing?).", jobId);
            return false;
        }
    }
}
