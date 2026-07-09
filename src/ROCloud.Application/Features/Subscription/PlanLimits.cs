using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Entities.Platform;

namespace ROCloud.Application.Features.Subscription;

/// <summary>
/// Enforces the tenant plan's per-resource creation caps so the limits shown on the subscription
/// page are real (guide §25). MaxUsers is enforced in UserProvisioning; this covers MaxCustomers and
/// MaxDeliveryBoys. 0 (Plan.Unlimited) means no cap. Counts rely on the tenant query filter (these
/// run inside a tenant-scoped request), matching GetSubscriptionQuery's usage figures.
/// </summary>
internal static class PlanLimits
{
    private const string DeliveryBoyRole = "DeliveryBoy";

    /// <summary>Throws when the tenant is already at/over its customer cap.</summary>
    public static async Task EnsureCanAddCustomerAsync(IAppDbContext db, ITenantContext tenant, CancellationToken ct)
    {
        var cap = await CapAsync(db, tenant, ct);
        if (cap is null || cap.MaxCustomers == Plan.Unlimited) return;
        var count = await db.Customers.CountAsync(ct);
        if (count >= cap.MaxCustomers)
            throw new PlanLimitException($"Upgrade required: max {cap.MaxCustomers} customers on the {cap.Name} plan.");
    }

    /// <summary>How many more customers the tenant may add (int.MaxValue when unlimited).</summary>
    public static async Task<int> CustomerHeadroomAsync(IAppDbContext db, ITenantContext tenant, CancellationToken ct)
    {
        var cap = await CapAsync(db, tenant, ct);
        if (cap is null || cap.MaxCustomers == Plan.Unlimited) return int.MaxValue;
        var count = await db.Customers.CountAsync(ct);
        return Math.Max(0, cap.MaxCustomers - count);
    }

    /// <summary>Throws when the tenant is already at/over its delivery-boy cap (active users on the role).</summary>
    public static async Task EnsureCanAddDeliveryBoyAsync(IAppDbContext db, ITenantContext tenant, CancellationToken ct)
    {
        var cap = await CapAsync(db, tenant, ct);
        if (cap is null || cap.MaxDeliveryBoys == Plan.Unlimited) return;
        var count = await db.Users.CountAsync(u => u.IsActive && u.Role != null && u.Role.Name == DeliveryBoyRole, ct);
        if (count >= cap.MaxDeliveryBoys)
            throw new PlanLimitException($"Upgrade required: max {cap.MaxDeliveryBoys} delivery boys on the {cap.Name} plan.");
    }

    private static Task<PlanCap?> CapAsync(IAppDbContext db, ITenantContext tenant, CancellationToken ct)
        => db.Tenants
            .Where(t => t.Id == tenant.TenantId)
            .Select(t => new PlanCap(t.Plan!.Name, t.Plan.MaxCustomers, t.Plan.MaxDeliveryBoys))
            .FirstOrDefaultAsync(ct);

    private sealed record PlanCap(string Name, int MaxCustomers, int MaxDeliveryBoys);
}
