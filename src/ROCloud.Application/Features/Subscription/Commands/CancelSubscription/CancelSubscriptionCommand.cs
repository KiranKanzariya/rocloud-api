using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Subscription.Commands.CancelSubscription;

/// <summary>
/// Cancels the tenant's subscription. Access is kept until the period they already paid for ends
/// (SubscriptionEndsAt, or TrialEndsAt on a trial) — TenantMiddleware only blocks a Cancelled tenant
/// once that date passes; then it stays Cancelled (the expiry job leaves it alone). Future Razorpay
/// charges are stopped now so cancelling never keeps billing the card. Reversible until the period
/// ends via ResumeSubscriptionCommand.
/// </summary>
public sealed record CancelSubscriptionCommand : IRequest;

public class CancelSubscriptionCommandHandler : IRequestHandler<CancelSubscriptionCommand>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IRazorpayService _razorpay;
    private readonly ILogger<CancelSubscriptionCommandHandler> _logger;

    public CancelSubscriptionCommandHandler(
        IAppDbContext db,
        ITenantContext tenant,
        IRazorpayService razorpay,
        ILogger<CancelSubscriptionCommandHandler> logger)
    {
        _db = db;
        _tenant = tenant;
        _razorpay = razorpay;
        _logger = logger;
    }

    public async Task Handle(CancelSubscriptionCommand request, CancellationToken ct)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == _tenant.TenantId, ct)
            ?? throw new NotFoundException("Tenant", _tenant.TenantId);

        // Stop future auto-charges at Razorpay (best-effort — never fail the cancel over this).
        if (!string.IsNullOrEmpty(tenant.RazorpaySubscriptionId))
        {
            try
            {
                await _razorpay.CancelSubscriptionAsync(tenant.RazorpaySubscriptionId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Razorpay cancel failed for subscription {SubId} (tenant {TenantId})",
                    tenant.RazorpaySubscriptionId, tenant.Id);
            }
        }

        tenant.Status = TenantStatus.Cancelled;
        tenant.RazorpaySubscriptionId = null;

        await _db.SaveChangesAsync(ct);
    }
}
