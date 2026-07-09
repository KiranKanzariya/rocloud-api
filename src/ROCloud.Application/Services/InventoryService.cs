using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Services;

/// <summary>
/// Default bottle-float tracker (guide §9). Mutates the EF change-tracker only — callers
/// call SaveChanges so the stock update shares the surrounding transaction.
/// </summary>
public class InventoryService : IInventoryService
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUserService _currentUser;

    public InventoryService(IAppDbContext db, ITenantContext tenant, ICurrentUserService currentUser)
    {
        _db = db;
        _tenant = tenant;
        _currentUser = currentUser;
    }

    public async Task RecordIssueAsync(
        Guid productId, int quantity, Guid? orderId, Guid? customerId, CancellationToken ct = default)
    {
        if (quantity <= 0) return;
        var inv = await GetOrCreateAsync(productId, ct);
        InventoryMath.Apply(inv, InventoryMovementType.Issue, quantity);
        AddMovement(productId, InventoryMovementType.Issue, quantity, orderId, customerId, null);
    }

    public async Task RecordReturnAsync(
        Guid productId, int quantity, Guid? orderId, Guid? customerId, CancellationToken ct = default)
    {
        if (quantity <= 0) return;
        var inv = await GetOrCreateAsync(productId, ct);
        InventoryMath.Apply(inv, InventoryMovementType.Return, quantity);
        AddMovement(productId, InventoryMovementType.Return, quantity, orderId, customerId, null);
    }

    public async Task RecordDamageAsync(Guid productId, int quantity, string? notes, CancellationToken ct = default)
    {
        if (quantity <= 0) return;
        var inv = await GetOrCreateAsync(productId, ct);
        InventoryMath.Apply(inv, InventoryMovementType.Damage, quantity);
        AddMovement(productId, InventoryMovementType.Damage, quantity, null, null, notes);
    }

    /// <summary>
    /// Finds the product's inventory row — first in the local change-tracker (so several
    /// calls in one unit of work share the row), then the store — creating it if absent.
    /// </summary>
    private async Task<Inventory> GetOrCreateAsync(Guid productId, CancellationToken ct)
    {
        var local = _db.Inventories.Local.FirstOrDefault(i => i.ProductId == productId);
        if (local is not null) return local;

        var existing = await _db.Inventories.FirstOrDefaultAsync(i => i.ProductId == productId, ct);
        if (existing is not null) return existing;

        var created = new Inventory
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            ProductId = productId,
            LastUpdated = DateTime.UtcNow
        };
        _db.Inventories.Add(created);
        return created;
    }

    private void AddMovement(
        Guid productId, InventoryMovementType type, int quantity, Guid? orderId, Guid? customerId, string? notes)
        => _db.InventoryMovements.Add(new InventoryMovement
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            ProductId = productId,
            OrderId = orderId,
            CustomerId = customerId,
            MovementType = type,
            Quantity = quantity,
            PerformedBy = _currentUser.UserId,
            Notes = notes
        });
}
