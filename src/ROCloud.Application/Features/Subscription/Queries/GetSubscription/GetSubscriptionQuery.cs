using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Subscription.Dtos;

namespace ROCloud.Application.Features.Subscription.Queries.GetSubscription;

/// <summary>The current tenant's subscription with plan limits and live usage counts.</summary>
public sealed record GetSubscriptionQuery : IRequest<SubscriptionDto>;

public class GetSubscriptionQueryHandler : IRequestHandler<GetSubscriptionQuery, SubscriptionDto>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;

    public GetSubscriptionQueryHandler(IAppDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<SubscriptionDto> Handle(GetSubscriptionQuery request, CancellationToken ct)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters().Include(t => t.Plan)
            .FirstOrDefaultAsync(t => t.Id == _tenant.TenantId, ct)
            ?? throw new NotFoundException("Tenant", _tenant.TenantId);
        var plan = tenant.Plan ?? throw new NotFoundException("Plan", tenant.PlanId);

        // Customers/Users are tenant-filtered automatically by the global query filter.
        var customers = await _db.Customers.CountAsync(ct);
        var users = await _db.Users.CountAsync(u => u.IsActive, ct);
        var deliveryBoys = await _db.Users
            .CountAsync(u => u.IsActive && u.Role != null && u.Role.Name == "DeliveryBoy", ct);

        var usage = new UsageDto(
            customers, plan.MaxCustomers,
            users, plan.MaxUsers,
            deliveryBoys, plan.MaxDeliveryBoys);

        var net = SubscriptionDiscountCalculator.Net(
            tenant.SubscriptionDiscountType, tenant.SubscriptionDiscountValue, plan.MonthlyPrice);

        return new SubscriptionDto(
            plan.Name, plan.PlanType.ToString(), plan.MonthlyPrice,
            tenant.Status.ToString(), tenant.TrialEndsAt, tenant.SubscriptionEndsAt, usage,
            tenant.SubscriptionDiscountType.ToString(), tenant.SubscriptionDiscountValue, net);
    }
}
