using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;

namespace ROCloud.Application.Features.Orders.Commands;

/// <summary>
/// Resolves which delivery boy should handle an order, given the customer's area. Preference:
///   1. an active DeliveryBoy explicitly assigned to that area (user_areas, Phase 13),
///   2. else the delivery boy most recently assigned to an order in the same area,
///   3. else the first active user with the "DeliveryBoy" role,
///   4. else null (order is created unassigned).
/// </summary>
internal static class DeliveryBoyResolver
{
    public const string DeliveryBoyRole = "DeliveryBoy";

    /// <summary>
    /// Active delivery-boy user ids for the current tenant (query filter scopes to the tenant). Load once
    /// and pass to the batch <see cref="ResolveAsync(IAppDbContext, Guid?, IReadOnlyList{Guid}, CancellationToken)"/>
    /// overload when resolving many orders, instead of re-querying this identical list per order.
    /// </summary>
    public static Task<List<Guid>> GetActiveDeliveryBoyIdsAsync(IAppDbContext db, CancellationToken ct) =>
        db.Users
            .Where(u => u.IsActive && u.Role != null && u.Role.Name == DeliveryBoyRole)
            .Select(u => u.Id)
            .ToListAsync(ct);

    /// <summary>Single-order convenience: loads the delivery-boy list, then resolves against it.</summary>
    public static async Task<Guid?> ResolveAsync(IAppDbContext db, Guid? areaId, CancellationToken ct)
        => await ResolveAsync(db, areaId, await GetActiveDeliveryBoyIdsAsync(db, ct), ct);

    /// <summary>Batch overload: resolves using a pre-loaded delivery-boy id list (no per-call user query).</summary>
    public static async Task<Guid?> ResolveAsync(
        IAppDbContext db, Guid? areaId, IReadOnlyList<Guid> deliveryBoyIds, CancellationToken ct)
    {
        if (deliveryBoyIds.Count == 0)
            return null;

        if (areaId is { } area)
        {
            // 1. A delivery boy explicitly assigned to this area.
            var assigned = await db.UserAreas
                .Where(ua => ua.AreaId == area && deliveryBoyIds.Contains(ua.UserId))
                .Select(ua => (Guid?)ua.UserId)
                .FirstOrDefaultAsync(ct);
            if (assigned is { } a)
                return a;

            // 2. Whoever most recently served this area.
            var recent = await db.Orders
                .Where(o => o.AreaId == area && o.DeliveryBoyId != null)
                .OrderByDescending(o => o.OrderDate)
                .Select(o => o.DeliveryBoyId)
                .FirstOrDefaultAsync(ct);
            if (recent is { } r && deliveryBoyIds.Contains(r))
                return r;
        }

        return deliveryBoyIds[0];
    }
}
