using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Invoices.Commands.BulkGenerateInvoices;
using ROCloud.Application.Features.Invoices.Commands.SendInvoice;
using ROCloud.Domain.Enums;

namespace ROCloud.Infrastructure.BackgroundJobs;

/// <summary>
/// Generates last month's invoices for every Monthly-billed customer of every active tenant,
/// then sends each one. Runs on the 1st of the month at 00:30.
/// </summary>
public class MonthlyBillingJob
{
    private readonly TenantJobRunner _runner;
    private readonly ILogger<MonthlyBillingJob> _logger;

    public MonthlyBillingJob(TenantJobRunner runner, ILogger<MonthlyBillingJob> logger)
    {
        _runner = runner;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var firstOfThisMonth = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var from = firstOfThisMonth.AddMonths(-1);
        var to = firstOfThisMonth.AddDays(-1);

        await _runner.ForEachTenantAsync(async (sp, tenantId, token) =>
        {
            var mediator = sp.GetRequiredService<IMediator>();
            var result = await mediator.Send(new BulkGenerateInvoicesCommand(from, to, null, null), token);
            _logger.LogInformation(
                "MonthlyBilling: tenant {TenantId} generated {Count} invoice(s) for {From}..{To}",
                tenantId, result.InvoicesCreated, from, to);

            // Send each freshly-generated invoice (best-effort; one failure shouldn't stop the rest).
            var db = sp.GetRequiredService<IAppDbContext>();
            // Send those not already fully paid (prior at-the-door payments may have settled some).
            var invoiceIds = await db.Invoices
                .Where(i => i.PeriodFrom == from
                    && (i.Status == InvoiceStatus.Draft || i.Status == InvoiceStatus.PartiallyPaid))
                .Select(i => i.Id)
                .ToListAsync(token);

            foreach (var id in invoiceIds)
            {
                try { await mediator.Send(new SendInvoiceCommand(id), token); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "MonthlyBilling: failed to send invoice {InvoiceId}", id);
                }
            }
        }, ct);
    }
}
