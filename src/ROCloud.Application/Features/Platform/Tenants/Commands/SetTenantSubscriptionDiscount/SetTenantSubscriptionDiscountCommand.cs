using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Platform.Tenants.Commands.SetTenantSubscriptionDiscount;

/// <summary>
/// Sets a tenant's standing discount on their ROCloud subscription price (guide §26). SuperAdmin
/// only. Percentage 0–100, Fixed ≥ 0, or None to clear. Applied to future subscription charges.
/// </summary>
public sealed record SetTenantSubscriptionDiscountCommand(
    Guid TenantId, string DiscountType, decimal DiscountValue) : IRequest;

public class SetTenantSubscriptionDiscountCommandValidator
    : AbstractValidator<SetTenantSubscriptionDiscountCommand>
{
    public SetTenantSubscriptionDiscountCommandValidator()
    {
        RuleFor(c => c.TenantId).NotEmpty();
        RuleFor(c => c.DiscountType)
            .Must(v => Enum.TryParse<SubscriptionDiscountType>(v, out _))
            .WithMessage("Discount type must be None, Percentage, or Fixed.");
        RuleFor(c => c.DiscountValue).GreaterThanOrEqualTo(0m);
        RuleFor(c => c.DiscountValue).LessThanOrEqualTo(100m)
            .When(c => string.Equals(c.DiscountType, "Percentage", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Percentage discount must be between 0 and 100.");
    }
}

public class SetTenantSubscriptionDiscountCommandHandler
    : IRequestHandler<SetTenantSubscriptionDiscountCommand>
{
    private readonly IAppDbContext _db;

    public SetTenantSubscriptionDiscountCommandHandler(IAppDbContext db) => _db = db;

    public async Task Handle(SetTenantSubscriptionDiscountCommand request, CancellationToken ct)
    {
        // tenants is not RLS-protected — a plain lookup works from the platform context.
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == request.TenantId && !t.IsDeleted, ct)
                     ?? throw new NotFoundException("Tenant", request.TenantId);

        var type = Enum.Parse<SubscriptionDiscountType>(request.DiscountType);
        tenant.SubscriptionDiscountType = type;
        tenant.SubscriptionDiscountValue = type == SubscriptionDiscountType.None ? 0m : request.DiscountValue;

        await _db.SaveChangesAsync(ct);
    }
}
