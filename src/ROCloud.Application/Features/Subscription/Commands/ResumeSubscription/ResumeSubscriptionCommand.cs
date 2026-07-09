using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Subscription.Commands.ResumeSubscription;

/// <summary>
/// Undoes a cancellation while the tenant is still within the period they paid for: restores
/// Active (or Trial if they cancelled during a trial). Returns false if there's nothing to resume
/// — not Cancelled, or the period has already ended (they must re-subscribe instead). Guide §25.
/// </summary>
public sealed record ResumeSubscriptionCommand : IRequest<bool>;

public class ResumeSubscriptionCommandHandler : IRequestHandler<ResumeSubscriptionCommand, bool>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;

    public ResumeSubscriptionCommandHandler(IAppDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<bool> Handle(ResumeSubscriptionCommand request, CancellationToken ct)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == _tenant.TenantId, ct)
            ?? throw new NotFoundException("Tenant", _tenant.TenantId);

        if (tenant.Status != TenantStatus.Cancelled) return false;

        // Only resumable while still inside the paid/trial period; otherwise it's a fresh subscribe.
        if (tenant.SubscriptionEndsAt is { } sub && sub >= DateTime.UtcNow)
            tenant.Status = TenantStatus.Active;
        else if (tenant.TrialEndsAt is { } trial && trial >= DateTime.UtcNow)
            tenant.Status = TenantStatus.Trial;
        else
            return false;

        await _db.SaveChangesAsync(ct);
        return true;
    }
}
