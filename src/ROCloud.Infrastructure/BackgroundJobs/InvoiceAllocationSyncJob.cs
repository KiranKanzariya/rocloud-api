using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Payments;
using ROCloud.Domain.Enums;

namespace ROCloud.Infrastructure.BackgroundJobs;

/// <summary>
/// Re-settles every customer's invoices against their payments, for every active tenant. Runs nightly
/// at 02:00.
///
/// Invoice paid-amounts and statuses are maintained on WRITE (<see cref="InvoiceAllocationSync"/>) so
/// that the invoice list and its status filter can be plain SQL. That is fast and simple, but it has
/// one weakness: a write path that forgets to call the sync leaves a stale status behind. This job is
/// the safety net — a full recompute is idempotent, so re-running it can only ever correct.
///
/// It is also the BACKFILL. Invoices that predate the sync were never materialised, so their status
/// still ignores payments the owner recorded against the customer rather than the invoice; the first
/// run puts every one of them right.
/// </summary>
public class InvoiceAllocationSyncJob
{
    private readonly TenantJobRunner _runner;
    private readonly ILogger<InvoiceAllocationSyncJob> _logger;

    public InvoiceAllocationSyncJob(TenantJobRunner runner, ILogger<InvoiceAllocationSyncJob> logger)
    {
        _runner = runner;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        await _runner.ForEachTenantAsync(async (sp, tenantId, token) =>
        {
            var db = sp.GetRequiredService<IAppDbContext>();

            // Only customers who actually hold an invoice — everyone else has nothing to re-state.
            var customerIds = await db.Invoices
                .Where(i => i.Status != InvoiceStatus.Cancelled)
                .Select(i => i.CustomerId)
                .Distinct()
                .ToListAsync(token);

            var synced = 0;
            foreach (var customerId in customerIds)
            {
                try
                {
                    await InvoiceAllocationSync.SyncAsync(db, customerId, token);
                    synced++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // One bad customer must not stop the rest of the tenant being reconciled.
                    _logger.LogWarning(ex,
                        "InvoiceAllocationSync: tenant {TenantId} customer {CustomerId} failed",
                        tenantId, customerId);
                }
            }

            _logger.LogInformation(
                "InvoiceAllocationSync: tenant {TenantId} re-settled {Count} customer(s)", tenantId, synced);
        }, ct);
    }
}
