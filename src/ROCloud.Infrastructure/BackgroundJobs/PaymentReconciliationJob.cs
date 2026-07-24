using Hangfire;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ROCloud.Application.Features.Payments.Commands.ReconcilePayments;

namespace ROCloud.Infrastructure.BackgroundJobs;

/// <summary>
/// Resolves online payments stuck in Pending, per tenant (guide §10). Runs frequently because the
/// anonymous Razorpay webhook can't read payments under RLS — reconciliation is the reliable
/// completion path. Dispatches ReconcilePaymentsCommand inside each tenant's scope (tenant GUC set),
/// so its RLS-protected payment reads succeed.
/// </summary>
public class PaymentReconciliationJob
{
    private readonly TenantJobRunner _runner;
    private readonly ILogger<PaymentReconciliationJob> _logger;

    public PaymentReconciliationJob(TenantJobRunner runner, ILogger<PaymentReconciliationJob> logger)
    {
        _runner = runner;
        _logger = logger;
    }

    /// <summary>
    /// Reconciles every tenant's pending payments. Serialised: the schedule fires every 15 minutes and
    /// each run makes one live Razorpay round-trip per pending payment across every tenant, so a slow
    /// run can still be in flight when the next is due. Without this, two runs would resolve the same
    /// payments concurrently and race each other's writes. A second run that finds the lock held is
    /// dropped rather than queued — the next tick picks the work up anyway.
    /// </summary>
    [DisableConcurrentExecution(timeoutInSeconds: 0)]
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        await _runner.ForEachTenantAsync(async (sp, tenantId, token) =>
        {
            var mediator = sp.GetRequiredService<IMediator>();
            var r = await mediator.Send(new ReconcilePaymentsCommand(), token);
            if (r.Completed + r.Failed + r.Duplicates > 0)
                _logger.LogInformation(
                    "PaymentReconcile: tenant {TenantId} completed={Completed} failed={Failed} duplicates={Duplicates}",
                    tenantId, r.Completed, r.Failed, r.Duplicates);
        }, ct);
    }
}
