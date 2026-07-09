using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Models;
using ROCloud.Application.Features.Inventory.Dtos;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Inventory.Queries.GetInventoryMovements;

public sealed record GetInventoryMovementsQuery(InventoryMovementFilterDto Filter)
    : IRequest<PagedResult<InventoryMovementDto>>;

public class GetInventoryMovementsQueryHandler
    : IRequestHandler<GetInventoryMovementsQuery, PagedResult<InventoryMovementDto>>
{
    private readonly IAppDbContext _db;

    public GetInventoryMovementsQueryHandler(IAppDbContext db) => _db = db;

    public async Task<PagedResult<InventoryMovementDto>> Handle(
        GetInventoryMovementsQuery request, CancellationToken ct)
    {
        var f = request.Filter;
        var page = Math.Max(1, f.Page);
        var pageSize = Math.Clamp(f.PageSize, 1, 200);

        IQueryable<InventoryMovement> query = _db.InventoryMovements;

        if (f.ProductId is { } productId) query = query.Where(m => m.ProductId == productId);
        if (f.CustomerId is { } customerId) query = query.Where(m => m.CustomerId == customerId);
        if (f.FromDate is { } from)
        {
            var fromTs = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(m => m.CreatedAt >= fromTs);
        }
        if (f.ToDate is { } to)
        {
            var toTs = to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            query = query.Where(m => m.CreatedAt <= toTs);
        }
        // MovementType may be a single value ("Return") or a comma-separated set ("Return,Damage").
        if (f.MovementType is not null)
        {
            var types = f.MovementType
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(v => Enum.GetNames<InventoryMovementType>().Contains(v))
                .Select(Enum.Parse<InventoryMovementType>)
                .ToList();
            if (types.Count > 0)
                query = query.Where(m => types.Contains(m.MovementType));
        }

        var total = await query.CountAsync(ct);

        var rows = await query
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(m => new
            {
                m.Id,
                m.ProductId,
                ProductName = m.Product != null ? m.Product.Name : string.Empty,
                m.MovementType,
                m.Quantity,
                m.OrderId,
                m.CustomerId,
                CustomerName = _db.Customers.Where(c => c.Id == m.CustomerId).Select(c => c.Name).FirstOrDefault(),
                m.PerformedBy,
                m.Notes,
                m.CreatedAt
            })
            .ToListAsync(ct);

        var items = rows.Select(r => new InventoryMovementDto(
            r.Id, r.ProductId, r.ProductName, r.MovementType.ToString(), r.Quantity,
            r.OrderId, r.CustomerId, r.CustomerName, r.PerformedBy, r.Notes, r.CreatedAt)).ToList();

        return new PagedResult<InventoryMovementDto>(items, total, page, pageSize);
    }
}
