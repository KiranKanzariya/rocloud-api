using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Platform.Tenants.Commands.SetTenantStatus;

/// <summary>Suspends or reactivates a tenant (platform admin action, guide §26).</summary>
public sealed record SetTenantStatusCommand(Guid TenantId, string Status) : IRequest;

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
        tenant.Status = status;

        // Reactivating a lapsed tenant must also push the subscription end date forward, otherwise the
        // daily SubscriptionExpiryJob would immediately flip it straight back to Overdue (guide §25/§26).
        if (status == TenantStatus.Active
            && (tenant.SubscriptionEndsAt is null || tenant.SubscriptionEndsAt < DateTime.UtcNow))
        {
            tenant.SubscriptionEndsAt = DateTime.UtcNow.AddMonths(1);
        }

        await _db.SaveChangesAsync(ct);
    }
}
