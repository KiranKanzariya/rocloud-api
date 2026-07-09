using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Platform.Plans.Dtos;

namespace ROCloud.Application.Features.Platform.Plans.Queries.GetAllPlans;

/// <summary>All plans (incl. inactive) with the number of tenants on each.</summary>
public sealed record GetAllPlansQuery : IRequest<IReadOnlyList<PlatformPlanDto>>;

public class GetAllPlansQueryHandler : IRequestHandler<GetAllPlansQuery, IReadOnlyList<PlatformPlanDto>>
{
    private readonly IAppDbContext _db;

    public GetAllPlansQueryHandler(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<PlatformPlanDto>> Handle(GetAllPlansQuery request, CancellationToken ct)
    {
        var counts = await _db.Tenants.Where(t => !t.IsDeleted)
            .GroupBy(t => t.PlanId)
            .Select(g => new { PlanId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PlanId, x => x.Count, ct);

        var plans = await _db.Plans.OrderBy(p => p.MonthlyPrice).ToListAsync(ct);

        return plans.Select(p => new PlatformPlanDto(
            p.Id, p.Name, p.PlanType.ToString(), p.MonthlyPrice, p.YearlyPrice,
            p.MaxCustomers, p.MaxUsers, p.MaxDeliveryBoys,
            p.WhatsappEnabled, p.CustomRolesEnabled, p.MultiBranchEnabled, p.ApiAccessEnabled,
            p.IsActive, counts.GetValueOrDefault(p.Id, 0))).ToList();
    }
}
