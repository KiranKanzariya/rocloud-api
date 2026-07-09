using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Inventory.Dtos;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Inventory.Queries.GetInventoryByProduct;

public sealed record GetInventoryByProductQuery(Guid ProductId) : IRequest<InventorySummaryDto>;

public class GetInventoryByProductQueryHandler : IRequestHandler<GetInventoryByProductQuery, InventorySummaryDto>
{
    private readonly IAppDbContext _db;

    public GetInventoryByProductQueryHandler(IAppDbContext db) => _db = db;

    public async Task<InventorySummaryDto> Handle(GetInventoryByProductQuery request, CancellationToken ct)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == request.ProductId, ct)
                      ?? throw new NotFoundException("Product", request.ProductId);

        var inv = await _db.Inventories
            .Where(i => i.ProductId == product.Id)
            .Select(i => new { i.TotalStock, i.IssuedStock, i.ReturnedStock, i.DamagedStock, i.LastUpdated })
            .FirstOrDefaultAsync(ct);

        return new InventorySummaryDto(
            product.Id, product.Name, product.BottleSize.ToWire(),
            inv?.TotalStock ?? 0,
            inv?.IssuedStock ?? 0,
            inv?.ReturnedStock ?? 0,
            inv?.DamagedStock ?? 0,
            (inv?.TotalStock ?? 0) - (inv?.IssuedStock ?? 0) - (inv?.DamagedStock ?? 0),
            inv?.LastUpdated);
    }
}
