using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Platform.Tenants.Commands.ChangeTenantPlan;

/// <summary>Platform admin override of a tenant's plan (no payment; guide §26). SuperAdmin only.</summary>
public sealed record ChangeTenantPlanCommand(Guid TenantId, string PlanType) : IRequest;

public class ChangeTenantPlanCommandValidator : AbstractValidator<ChangeTenantPlanCommand>
{
    public ChangeTenantPlanCommandValidator()
    {
        RuleFor(c => c.TenantId).NotEmpty();
        RuleFor(c => c.PlanType)
            .Must(v => Enum.TryParse<PlanType>(v, out _))
            .WithMessage("Invalid plan type.");
    }
}

public class ChangeTenantPlanCommandHandler : IRequestHandler<ChangeTenantPlanCommand>
{
    private readonly IAppDbContext _db;

    public ChangeTenantPlanCommandHandler(IAppDbContext db) => _db = db;

    public async Task Handle(ChangeTenantPlanCommand request, CancellationToken ct)
    {
        var planType = Enum.Parse<PlanType>(request.PlanType);
        var plan = await _db.Plans.FirstOrDefaultAsync(p => p.PlanType == planType && p.IsActive, ct)
                   ?? throw new NotFoundException("Plan", request.PlanType);

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == request.TenantId && !t.IsDeleted, ct)
                     ?? throw new NotFoundException("Tenant", request.TenantId);

        // Don't downgrade a tenant below its current usage (guide §26).
        await Subscription.PlanChangeGuard.EnsureUsageFitsAsync(_db, tenant.Id, plan, ct);

        tenant.PlanId = plan.Id;

        // An open renewal invoice quotes the plan the tenant had a moment ago. Cancelling it lets the
        // renewal job re-raise one at the new plan's price; leaving it would both bill the wrong amount
        // and block that correct invoice from ever being raised.
        await Subscription.Services.OpenSubscriptionInvoices.CancelAsync(
            _db, tenant.Id,
            $"Your plan was changed to {plan.Name} by ROCloud. A new invoice will be raised at the {plan.Name} price.",
            ct);

        await _db.SaveChangesAsync(ct);
    }
}
