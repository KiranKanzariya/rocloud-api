using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Products.Dtos;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Products.Queries.GetProducts;

/// <summary>Lists the tenant's products. By default active only; pass includeInactive=true for all.</summary>
public sealed record GetProductsQuery(bool IncludeInactive = false) : IRequest<IReadOnlyList<ProductDto>>;

public class GetProductsQueryHandler : IRequestHandler<GetProductsQuery, IReadOnlyList<ProductDto>>
{
    private readonly IAppDbContext _db;

    public GetProductsQueryHandler(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<ProductDto>> Handle(GetProductsQuery request, CancellationToken ct)
    {
        var query = _db.Products.AsQueryable();
        if (!request.IncludeInactive)
            query = query.Where(p => p.IsActive);

        var rows = await query
            .OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Name, p.BottleSize, p.DefaultRate, p.Unit, p.IsActive, p.CreatedAt })
            .ToListAsync(ct);

        return rows.Select(p => new ProductDto(
            p.Id, p.Name, p.BottleSize.ToWire(), p.DefaultRate, p.Unit, p.IsActive, p.CreatedAt)).ToList();
    }
}
