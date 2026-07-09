using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Inventory.Dtos;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Inventory.Queries.GetInventory;

/// <summary>Per-product stock summary for every active product (zeros when no inventory row exists yet).</summary>
public sealed record GetInventoryQuery : IRequest<IReadOnlyList<InventorySummaryDto>>;

public class GetInventoryQueryHandler : IRequestHandler<GetInventoryQuery, IReadOnlyList<InventorySummaryDto>>
{
    private readonly IAppDbContext _db;

    public GetInventoryQueryHandler(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<InventorySummaryDto>> Handle(GetInventoryQuery request, CancellationToken ct)
    {
        var rows = await _db.Products
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.BottleSize,
                Inv = _db.Inventories
                    .Where(i => i.ProductId == p.Id)
                    .Select(i => new { i.TotalStock, i.IssuedStock, i.ReturnedStock, i.DamagedStock, i.LastUpdated })
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        return rows.Select(r => new InventorySummaryDto(
            r.Id, r.Name, r.BottleSize.ToWire(),
            r.Inv?.TotalStock ?? 0,
            r.Inv?.IssuedStock ?? 0,
            r.Inv?.ReturnedStock ?? 0,
            r.Inv?.DamagedStock ?? 0,
            (r.Inv?.TotalStock ?? 0) - (r.Inv?.IssuedStock ?? 0) - (r.Inv?.DamagedStock ?? 0),
            r.Inv?.LastUpdated)).ToList();
    }
}
