using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Deliveries.Dtos;
using ROCloud.Application.Features.Deliveries.Queries.GetDeliveries;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Deliveries.Queries.GetDeliverySummary;

/// <summary>Per-delivery-boy pending/inTransit/delivered counts with completion %.</summary>
public sealed record GetDeliverySummaryQuery(DeliveryFilterDto Filter) : IRequest<IReadOnlyList<DeliverySummaryRowDto>>;

public class GetDeliverySummaryQueryHandler
    : IRequestHandler<GetDeliverySummaryQuery, IReadOnlyList<DeliverySummaryRowDto>>
{
    private readonly IAppDbContext _db;

    public GetDeliverySummaryQueryHandler(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<DeliverySummaryRowDto>> Handle(
        GetDeliverySummaryQuery request, CancellationToken ct)
    {
        var query = GetDeliveriesQueryHandler.ApplyFilter(_db.Deliveries, request.Filter);

        var grouped = await query
            .GroupBy(d => d.DeliveryBoyId)
            .Select(g => new
            {
                DeliveryBoyId = g.Key,
                Total = g.Count(),
                Pending = g.Count(d => d.Status == DeliveryStatus.Pending),
                InTransit = g.Count(d => d.Status == DeliveryStatus.InTransit),
                Delivered = g.Count(d => d.Status == DeliveryStatus.Delivered),
                Failed = g.Count(d => d.Status == DeliveryStatus.Failed)
            })
            .ToListAsync(ct);

        // Resolve names in one round-trip.
        var ids = grouped.Where(r => r.DeliveryBoyId != null).Select(r => r.DeliveryBoyId!.Value).ToList();
        var names = await _db.Users
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Name, ct);

        return grouped
            .Select(r => new DeliverySummaryRowDto(
                r.DeliveryBoyId,
                r.DeliveryBoyId is { } id && names.TryGetValue(id, out var name) ? name : "Unassigned",
                r.Total, r.Pending, r.InTransit, r.Delivered, r.Failed,
                r.Total == 0 ? 0 : Math.Round(r.Delivered * 100.0 / r.Total, 1)))
            .OrderByDescending(r => r.Total)
            .ToList();
    }
}
