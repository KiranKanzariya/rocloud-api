using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Settings;

namespace ROCloud.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Injects the current tenant id into the PostgreSQL session variable
/// <c>app.current_tenant_id</c> on every connection open, so the Row-Level Security
/// policies (guide §10.7 Layer 2) enforce tenant isolation as defence-in-depth.
///
/// Set on every open (rather than once in middleware) so it is correct regardless of
/// connection pooling. An empty/unresolved tenant is written as the all-zero GUID, which
/// matches no tenant — never left stale from a previous request.
///
/// Also pins the session timezone to the configured zone (App:TimeZone, default IST) so any in-SQL
/// calendar-date derivation EF emits (e.g. EXTRACT(YEAR/MONTH FROM …), date_trunc, <c>::date</c> on
/// a timestamptz column) is computed in that zone and is independent of the host machine's timezone
/// — matching what the portals display. Timestamps are still stored as UTC; this only affects how
/// SQL buckets them.
/// </summary>
public class TenantConnectionInterceptor : DbConnectionInterceptor
{
    private readonly ITenantContext _tenantContext;
    private readonly IAppSettings _settings;

    public TenantConnectionInterceptor(ITenantContext tenantContext, IAppSettings settings)
    {
        _tenantContext = tenantContext;
        _settings = settings;
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var cmd = CreateSetConfigCommand(connection);
        cmd.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var cmd = CreateSetConfigCommand(connection);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private DbCommand CreateSetConfigCommand(DbConnection connection)
    {
        var cmd = connection.CreateCommand();
        // set_config(name, value, is_local=false) — session-scoped, parameterised. The second call
        // pins the session timezone (App:TimeZone) so date bucketing in EF-generated SQL is
        // host-timezone-independent. set_config takes the value as a parameter, so the configured
        // zone is bound safely (no SQL injection). Both are session-scoped and re-applied on every
        // open, so pooled connections never carry stale state.
        cmd.CommandText =
            "SELECT set_config('app.current_tenant_id', @tenantId, false), set_config('TimeZone', @timeZone, false)";
        var p = cmd.CreateParameter();
        p.ParameterName = "tenantId";
        p.Value = _tenantContext.TenantId.ToString();
        cmd.Parameters.Add(p);
        var tz = cmd.CreateParameter();
        tz.ParameterName = "timeZone";
        tz.Value = _settings.TimeZone;
        cmd.Parameters.Add(tz);
        return cmd;
    }
}
