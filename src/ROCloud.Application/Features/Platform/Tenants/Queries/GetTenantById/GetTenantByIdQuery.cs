using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Platform.Tenants.Dtos;

namespace ROCloud.Application.Features.Platform.Tenants.Queries.GetTenantById;

/// <summary>Full detail for one tenant, including live customer/user usage counts.</summary>
public sealed record GetTenantByIdQuery(Guid Id) : IRequest<TenantDetailDto>;

public class GetTenantByIdQueryHandler : IRequestHandler<GetTenantByIdQuery, TenantDetailDto>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;

    public GetTenantByIdQueryHandler(IAppDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<TenantDetailDto> Handle(GetTenantByIdQuery request, CancellationToken ct)
    {
        var t = await _db.Tenants.Include(x => x.Plan)
            .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, ct)
            ?? throw new NotFoundException("Tenant", request.Id);

        // customers is RLS-protected — scope the connection to this tenant so the count is real.
        _tenant.TenantId = t.Id;
        var customers = await _db.Customers.CountAsync(c => c.TenantId == t.Id && !c.IsDeleted, ct);
        var users = await _db.Users.IgnoreQueryFilters()
            .CountAsync(u => u.TenantId == t.Id && !u.IsDeleted, ct);

        var net = Application.Features.Subscription.SubscriptionDiscountCalculator.Net(
            t.SubscriptionDiscountType, t.SubscriptionDiscountValue, t.Plan!.MonthlyPrice);

        // Lifetime billing (platform_billing_transactions has no RLS — safe to aggregate directly).
        var totalPaid = await _db.PlatformBillingTransactions
            .Where(b => b.TenantId == t.Id && b.Status == "Paid")
            .SumAsync(b => (decimal?)b.Amount, ct) ?? 0m;
        var billingCount = await _db.PlatformBillingTransactions.CountAsync(b => b.TenantId == t.Id, ct);

        return new TenantDetailDto(
            t.Id, t.Name, t.Subdomain, t.Plan!.Name, t.Plan.PlanType.ToString(), t.Status.ToString(),
            t.OwnerName, t.OwnerEmail, t.OwnerMobile,
            t.GstNumber, t.GstEnabled, Math.Round(t.GstRate * 100m, 2),
            t.AddressLine, t.City, t.State, t.Pincode,
            customers, users, t.TrialEndsAt, t.SubscriptionEndsAt, t.CreatedAt,
            t.Plan.MonthlyPrice, t.SubscriptionDiscountType.ToString(), t.SubscriptionDiscountValue, net,
            totalPaid, billingCount);
    }
}
