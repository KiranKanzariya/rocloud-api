using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Plans.Dtos;

namespace ROCloud.Application.Features.Plans.Queries.GetPlans;

/// <summary>Lists the active subscription plans (Basic / Pro / Enterprise), cheapest first.</summary>
public sealed record GetPlansQuery : IRequest<IReadOnlyList<PlanDto>>;

public class GetPlansQueryHandler : IRequestHandler<GetPlansQuery, IReadOnlyList<PlanDto>>
{
    private readonly IAppDbContext _db;

    public GetPlansQueryHandler(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<PlanDto>> Handle(GetPlansQuery request, CancellationToken ct)
    {
        return await _db.Plans
            .Where(p => p.IsActive)
            .OrderBy(p => p.MonthlyPrice)
            .Select(p => new PlanDto(
                p.Id, p.Name, p.PlanType.ToString(), p.MonthlyPrice, p.YearlyPrice,
                p.MaxCustomers, p.MaxUsers, p.MaxDeliveryBoys,
                p.WhatsappEnabled, p.CustomRolesEnabled, p.MultiBranchEnabled, p.ApiAccessEnabled))
            .ToListAsync(ct);
    }
}
