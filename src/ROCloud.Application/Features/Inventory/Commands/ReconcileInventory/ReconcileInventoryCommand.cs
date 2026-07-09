using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Inventory.Commands.ReconcileInventory;

/// <summary>
/// Recalculates every product's stock counters from the movement ledger (the source of
/// truth — delivery-driven Issue/Return are already recorded there) to fix any drift.
/// </summary>
public sealed record ReconcileInventoryCommand : IRequest<ReconcileResultDto>;

public sealed record ReconcileResultDto(int ProductsReconciled, int Discrepancies);

public class ReconcileInventoryCommandHandler : IRequestHandler<ReconcileInventoryCommand, ReconcileResultDto>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;

    public ReconcileInventoryCommandHandler(IAppDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<ReconcileResultDto> Handle(ReconcileInventoryCommand request, CancellationToken ct)
    {
        // Aggregate the ledger per product.
        var totals = await _db.InventoryMovements
            .GroupBy(m => m.ProductId)
            .Select(g => new
            {
                ProductId = g.Key,
                Issued = g.Where(m => m.MovementType == InventoryMovementType.Issue).Sum(m => m.Quantity),
                Returned = g.Where(m => m.MovementType == InventoryMovementType.Return).Sum(m => m.Quantity),
                Damaged = g.Where(m => m.MovementType == InventoryMovementType.Damage).Sum(m => m.Quantity),
                Restocked = g.Where(m => m.MovementType == InventoryMovementType.Restock).Sum(m => m.Quantity),
                Adjusted = g.Where(m => m.MovementType == InventoryMovementType.Adjustment).Sum(m => m.Quantity)
            })
            .ToListAsync(ct);

        var existing = await _db.Inventories.ToDictionaryAsync(i => i.ProductId, ct);

        var reconciled = 0;
        var discrepancies = 0;

        foreach (var t in totals)
        {
            var total = t.Restocked + t.Adjusted;
            var issued = t.Issued - t.Returned;   // net jars currently out with customers
            var returned = t.Returned;
            var damaged = t.Damaged;

            if (!existing.TryGetValue(t.ProductId, out var inv))
            {
                inv = new Domain.Entities.Tenant.Inventory
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenant.TenantId,
                    ProductId = t.ProductId
                };
                _db.Inventories.Add(inv);
            }

            if (inv.TotalStock != total || inv.IssuedStock != issued ||
                inv.ReturnedStock != returned || inv.DamagedStock != damaged)
            {
                discrepancies++;
            }

            inv.TotalStock = total;
            inv.IssuedStock = issued;
            inv.ReturnedStock = returned;
            inv.DamagedStock = damaged;
            inv.LastUpdated = DateTime.UtcNow;
            reconciled++;
        }

        await _db.SaveChangesAsync(ct);
        return new ReconcileResultDto(reconciled, discrepancies);
    }
}
