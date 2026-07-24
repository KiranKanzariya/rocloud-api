using ROCloud.Application.Common;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ROCloud.Application.Features.Orders.Commands.BulkCreateOrders;

namespace ROCloud.Infrastructure.BackgroundJobs;

/// <summary>
/// Creates the next day's orders from active subscriptions for every active tenant.
/// Runs every day at 23:59.
/// </summary>
public class DailyDeliveryRolloverJob
{
    private readonly TenantJobRunner _runner;
    private readonly ILogger<DailyDeliveryRolloverJob> _logger;

    public DailyDeliveryRolloverJob(TenantJobRunner runner, ILogger<DailyDeliveryRolloverJob> logger)
    {
        _runner = runner;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var tomorrow = AppTimeZone.Today(DateTime.UtcNow).AddDays(1);

        await _runner.ForEachTenantAsync(async (sp, tenantId, token) =>
        {
            var mediator = sp.GetRequiredService<IMediator>();
            var result = await mediator.Send(new BulkCreateOrdersCommand(tomorrow), token);
            _logger.LogInformation(
                "DailyRollover: tenant {TenantId} created {Count} order(s) for {Date}",
                tenantId, result.OrdersCreated, tomorrow);
        }, ct);
    }
}
