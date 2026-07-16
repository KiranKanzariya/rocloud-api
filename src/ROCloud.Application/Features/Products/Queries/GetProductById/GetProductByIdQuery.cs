using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Products.Dtos;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Products.Queries.GetProductById;

public sealed record GetProductByIdQuery(Guid Id) : IRequest<ProductDto>;

public class GetProductByIdQueryHandler : IRequestHandler<GetProductByIdQuery, ProductDto>
{
    private readonly IAppDbContext _db;

    public GetProductByIdQueryHandler(IAppDbContext db) => _db = db;

    public async Task<ProductDto> Handle(GetProductByIdQuery request, CancellationToken ct)
    {
        var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == request.Id, ct)
                ?? throw new NotFoundException("Product", request.Id);

        return new ProductDto(p.Id, p.Name, p.BottleSize.ToWire(), p.DefaultRate, p.Unit, p.Hsn, p.IsActive, p.CreatedAt);
    }
}
