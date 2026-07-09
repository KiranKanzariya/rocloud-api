using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Entities.Platform;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Features.Subscription;

/// <summary>
/// Shared plan-change safety check (guide §25/§26). Refuses a downgrade that would drop a tenant
/// below its current usage on ANY enforced cap — team members (MaxUsers), delivery boys
/// (MaxDeliveryBoys) and customers (MaxCustomers) — otherwise the tenant silently lands over-limit
/// and can only stop adding, never being brought back into compliance. All three caps are enforced
/// on creation (see <see cref="PlanLimits"/> and UserProvisioning), so the downgrade guard matches
/// them exactly. Counts use IgnoreQueryFilters + an explicit tenant id so this is correct on the
/// platform admin path too, where no ambient tenant context is set.
/// </summary>
internal static class PlanChangeGuard
{
    private const string DeliveryBoyRole = "DeliveryBoy";

    public static async Task EnsureUsageFitsAsync(IAppDbContext db, Guid tenantId, Plan target, CancellationToken ct)
    {
        var problems = new List<string>();

        // Team members — all non-deleted users in the tenant (matches UserProvisioning's MaxUsers check).
        if (target.MaxUsers != Plan.Unlimited)
        {
            var userCount = await db.Users.IgnoreQueryFilters()
                .CountAsync(u => u.TenantId == tenantId && !u.IsDeleted, ct);
            if (userCount > target.MaxUsers)
                problems.Add($"{userCount} team members (max {target.MaxUsers})");
        }

        // Delivery boys — active users on the DeliveryBoy role (matches PlanLimits.EnsureCanAddDeliveryBoy).
        if (target.MaxDeliveryBoys != Plan.Unlimited)
        {
            var deliveryCount = await db.Users.IgnoreQueryFilters()
                .CountAsync(u => u.TenantId == tenantId && !u.IsDeleted && u.IsActive
                                 && u.Role != null && u.Role.Name == DeliveryBoyRole, ct);
            if (deliveryCount > target.MaxDeliveryBoys)
                problems.Add($"{deliveryCount} delivery boys (max {target.MaxDeliveryBoys})");
        }

        // Customers — all non-deleted customers (matches PlanLimits.EnsureCanAddCustomer).
        if (target.MaxCustomers != Plan.Unlimited)
        {
            var customerCount = await db.Customers.IgnoreQueryFilters()
                .CountAsync(c => c.TenantId == tenantId && !c.IsDeleted, ct);
            if (customerCount > target.MaxCustomers)
                problems.Add($"{customerCount} customers (max {target.MaxCustomers})");
        }

        if (problems.Count > 0)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["plan"] =
                [
                    $"This account exceeds the {target.Name} plan on: {string.Join("; ", problems)}. " +
                    "Reduce these before switching to this plan."
                ]
            });
    }
}
