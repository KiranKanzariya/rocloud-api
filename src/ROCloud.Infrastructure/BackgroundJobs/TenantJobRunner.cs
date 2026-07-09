using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Enums;

namespace ROCloud.Infrastructure.BackgroundJobs;

/// <summary>
/// Runs work once per operational tenant inside its own DI scope with the tenant context set,
/// so the TenantConnectionInterceptor pins app.current_tenant_id and RLS-scoped reads/writes
/// behave exactly as they do in a normal request. This is how background jobs (which have no
/// HTTP/tenant context) safely touch tenant data.
///
/// "Operational" = Active/Trial, plus Overdue tenants still inside their payment grace window —
/// the same window during which TenantMiddleware still serves their API (so operational jobs and
/// the app stay in lock-step; once the paywall arms, jobs stop too). Suspended/Cancelled and
/// past-grace Overdue tenants are skipped.
/// </summary>
public class TenantJobRunner
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TenantJobRunner> _logger;
    private readonly int _overdueGraceDays;
    private readonly int _trialGraceDays;

    public TenantJobRunner(
        IServiceScopeFactory scopeFactory, ILogger<TenantJobRunner> logger, IConfiguration config)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        // Mirror TenantMiddleware's grace windows exactly.
        _overdueGraceDays = int.TryParse(config["Subscription:OverdueGraceDays"], out var g) ? g : 7;
        _trialGraceDays = int.TryParse(config["Subscription:TrialGraceDays"], out var tg) ? tg : 2;
    }

    public async Task ForEachTenantAsync(
        Func<IServiceProvider, Guid, CancellationToken, Task> work, CancellationToken ct = default)
    {
        var tenantIds = await GetOperationalTenantIdsAsync(ct);

        foreach (var tenantId in tenantIds)
        {
            using var scope = _scopeFactory.CreateScope();
            // Pin the scope to this tenant — the interceptor reads ITenantContext on connection open.
            scope.ServiceProvider.GetRequiredService<ITenantContext>().TenantId = tenantId;

            try
            {
                await work(scope.ServiceProvider, tenantId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One tenant's failure must not abort the whole run.
                _logger.LogError(ex, "Background job failed for tenant {TenantId}", tenantId);
            }
        }
    }

    private async Task<List<Guid>> GetOperationalTenantIdsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        var now = DateTime.UtcNow;
        var overdueCutoff = now.AddDays(-_overdueGraceDays); // paid lapse: SubscriptionEndsAt-based grace
        var trialCutoff = now.AddDays(-_trialGraceDays);     // trial lapse: TrialEndsAt-based grace

        return await db.Tenants
            .Where(t =>
                t.Status == TenantStatus.Active
                || t.Status == TenantStatus.Trial
                // Overdue but still within grace — mirrors TenantMiddleware: a paid lapse uses the
                // overdue grace off SubscriptionEndsAt; a straight-from-trial lapse (no paid end)
                // uses the shorter trial grace off TrialEndsAt. Past grace → excluded (paywall armed).
                || (t.Status == TenantStatus.Overdue
                    && (t.SubscriptionEndsAt != null
                            ? t.SubscriptionEndsAt >= overdueCutoff
                            : t.TrialEndsAt == null || t.TrialEndsAt >= trialCutoff)))
            .Select(t => t.Id)
            .ToListAsync(ct);
    }
}
