using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Customers.Dtos;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Customers.Queries.GetCustomerJarBalance;

/// <summary>
/// Net returnable jars a customer still holds, per product: Σ(Issue) − Σ(Return) across their
/// inventory movements (guide §9). Surfaces empties not yet brought back — including those issued
/// via plant pickup, which have no delivery-boy visit to record the return.
/// </summary>
public sealed record GetCustomerJarBalanceQuery(Guid CustomerId) : IRequest<IReadOnlyList<CustomerJarBalanceDto>>;

public class GetCustomerJarBalanceQueryHandler
    : IRequestHandler<GetCustomerJarBalanceQuery, IReadOnlyList<CustomerJarBalanceDto>>
{
    private readonly IAppDbContext _db;

    public GetCustomerJarBalanceQueryHandler(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<CustomerJarBalanceDto>> Handle(
        GetCustomerJarBalanceQuery request, CancellationToken ct)
    {
        // Net per product = issued to this customer minus what they've returned (good Returns) and
        // minus what they returned broken (customer-scoped Damages — those jars left their hands too).
        // Movements are tenant-scoped by the global query filter.
        var grouped = await _db.InventoryMovements
            .Where(m => m.CustomerId == request.CustomerId
                && (m.MovementType == InventoryMovementType.Issue
                    || m.MovementType == InventoryMovementType.Return
                    || m.MovementType == InventoryMovementType.Damage))
            .GroupBy(m => m.ProductId)
            .Select(g => new
            {
                ProductId = g.Key,
                Outstanding = g.Sum(m => m.MovementType == InventoryMovementType.Issue ? m.Quantity : -m.Quantity)
            })
            .Where(x => x.Outstanding > 0)
            .ToListAsync(ct);

        if (grouped.Count == 0)
            return [];

        var ids = grouped.Select(x => x.ProductId).ToList();
        var products = await _db.Products
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.BottleSize })
            .ToListAsync(ct);

        return grouped
            .Select(x =>
            {
                var p = products.First(pp => pp.Id == x.ProductId);
                return new CustomerJarBalanceDto(x.ProductId, p.Name, p.BottleSize.ToWire(), x.Outstanding);
            })
            .OrderByDescending(d => d.Outstanding)
            .ToList();
    }
}
