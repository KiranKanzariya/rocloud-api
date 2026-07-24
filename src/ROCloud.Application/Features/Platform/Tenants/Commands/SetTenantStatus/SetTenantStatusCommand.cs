using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Platform.Tenants.Commands.SetTenantStatus;

/// <summary>
/// Suspends or reactivates a tenant (platform admin action, guide §26).
/// <paramref name="CreditSuspendedDays"/> (reactivate only) gives a PAYING tenant back the days it lost
/// while suspended — opt-in, because a non-payment or abuse suspension should not be compensated.
/// </summary>
public sealed record SetTenantStatusCommand(
    Guid TenantId, string Status, bool CreditSuspendedDays = false) : IRequest;

public class SetTenantStatusCommandValidator : AbstractValidator<SetTenantStatusCommand>
{
    public SetTenantStatusCommandValidator()
    {
        RuleFor(c => c.TenantId).NotEmpty();
        RuleFor(c => c.Status)
            .Must(v => v is "Active" or "Suspended")
            .WithMessage("Status must be Active or Suspended.");
    }
}

public class SetTenantStatusCommandHandler : IRequestHandler<SetTenantStatusCommand>
{
    private readonly IAppDbContext _db;

    public SetTenantStatusCommandHandler(IAppDbContext db) => _db = db;

    public async Task Handle(SetTenantStatusCommand request, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == request.TenantId && !t.IsDeleted, ct)
                     ?? throw new NotFoundException("Tenant", request.TenantId);

        var status = Enum.Parse<TenantStatus>(request.Status);
        var now = DateTime.UtcNow;

        if (status == TenantStatus.Suspended)
        {
            // Stamp when the block began so a later reactivation can credit the paid days lost. Only on
            // the transition IN — re-suspending an already-suspended tenant must not restart the clock.
            tenant.SuspendedAt ??= now;
        }
        else if (status == TenantStatus.Active)
        {
            // Opt-in, admin-chosen: hand back the paid days lost while suspended. Deliberately NOT
            // automatic — a suspension for non-payment or abuse should keep its bite; only an
            // accidental/investigative block deserves the time back, and only the admin knows which.
            // Credited up to the paid period the tenant actually had left, so a tenant whose
            // subscription had ALREADY expired before being suspended earns nothing.
            if (request.CreditSuspendedDays && tenant.SuspendedAt is { } from && tenant.SubscriptionEndsAt is { } ends)
            {
                var lostUntil = ends < now ? ends : now;
                var credit = lostUntil - from;
                if (credit > TimeSpan.Zero) tenant.SubscriptionEndsAt = ends + credit;
            }

            // No SubscriptionEndsAt but a TrialEndsAt → the tenant never paid; it was on a FREE TRIAL.
            // Restore Trial and leave the dates alone: a running trial resumes, an expired one is picked
            // up as Overdue by the nightly job.
            if (tenant.SubscriptionEndsAt is null && tenant.TrialEndsAt is not null)
            {
                status = TenantStatus.Trial;
            }
            // A paid subscription that has lapsed comes back OVERDUE so the normal dunning ladder
            // resumes and the owner is asked to pay. Reactivating must never gift a month of service
            // nobody paid for (which a blanket "+1 month" used to do).
            else if (tenant.SubscriptionEndsAt is null || tenant.SubscriptionEndsAt < now)
            {
                status = TenantStatus.Overdue;
            }

            tenant.SuspendedAt = null;   // no longer suspended, whatever the resulting status
        }

        tenant.Status = status;

        await _db.SaveChangesAsync(ct);
    }
}
