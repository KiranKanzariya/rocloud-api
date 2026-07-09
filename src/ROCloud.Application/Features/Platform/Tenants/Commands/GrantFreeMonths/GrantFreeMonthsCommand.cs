using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Platform.Tenants.Commands.GrantFreeMonths;

/// <summary>
/// Grants a tenant N free months of ROCloud subscription by extending subscription_ends_at and
/// activating the account — no charge recorded (guide §26). SuperAdmin only. The extension is added
/// on top of whatever the tenant already has (trial or paid end date), never shortening it.
/// </summary>
public sealed record GrantFreeMonthsCommand(Guid TenantId, int Months) : IRequest<DateTime>;

public class GrantFreeMonthsCommandValidator : AbstractValidator<GrantFreeMonthsCommand>
{
    public GrantFreeMonthsCommandValidator()
    {
        RuleFor(c => c.TenantId).NotEmpty();
        RuleFor(c => c.Months).InclusiveBetween(1, 60)
            .WithMessage("Free months must be between 1 and 60.");
    }
}

public class GrantFreeMonthsCommandHandler : IRequestHandler<GrantFreeMonthsCommand, DateTime>
{
    private readonly IAppDbContext _db;

    public GrantFreeMonthsCommandHandler(IAppDbContext db) => _db = db;

    public async Task<DateTime> Handle(GrantFreeMonthsCommand request, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == request.TenantId && !t.IsDeleted, ct)
                     ?? throw new NotFoundException("Tenant", request.TenantId);

        var now = DateTime.UtcNow;
        // Extend from the latest of: now, current paid end, current trial end — never shorten.
        var basis = new[] { now, tenant.SubscriptionEndsAt ?? now, tenant.TrialEndsAt ?? now }.Max();

        tenant.SubscriptionEndsAt = basis.AddMonths(request.Months);
        tenant.Status = TenantStatus.Active;
        tenant.TrialEndsAt = null;

        await _db.SaveChangesAsync(ct);
        return tenant.SubscriptionEndsAt.Value;
    }
}
