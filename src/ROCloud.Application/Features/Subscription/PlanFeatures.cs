using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Subscription;

/// <summary>
/// Enforces the tenant plan's boolean feature flags — the on/off capabilities set per plan in the
/// admin portal, as opposed to the numeric caps that live in <see cref="PlanLimits"/>. A flag that is
/// off means the capability is not sold on that plan, so the action is blocked (Ensure*) or silently
/// skipped (background sends). The plan is resolved via the tenant, mirroring PlanLimits. Public (not
/// internal like PlanLimits) so the Infrastructure reminder jobs can reuse the WhatsApp check.
///
/// Only the flags with a real feature behind them are gated here: CustomRolesEnabled and
/// WhatsappEnabled. MultiBranchEnabled and ApiAccessEnabled are future scope — the features do not
/// exist yet, so there is nothing to gate; those flags are marked "Coming soon" in the UI instead.
/// </summary>
public static class PlanFeatures
{
    /// <summary>Throws when the tenant's plan does not include custom roles.</summary>
    public static async Task EnsureCustomRolesAsync(IAppDbContext db, ITenantContext tenant, CancellationToken ct)
    {
        var plan = await PlanOf(db, tenant.TenantId, ct);
        if (plan is null || !plan.CustomRolesEnabled)
            throw new PlanLimitException(
                $"Upgrade required: custom roles are not available on the {plan?.Name ?? "current"} plan.");
    }

    /// <summary>True when the tenant's plan includes WhatsApp messaging. Reminder jobs skip sending when false.</summary>
    public static async Task<bool> WhatsAppEnabledAsync(IAppDbContext db, Guid tenantId, CancellationToken ct)
    {
        var plan = await PlanOf(db, tenantId, ct);
        return plan?.WhatsappEnabled ?? false;
    }

    /// <summary>
    /// The tenant's current tier, read from the DB. Callers must not rank the JWT's plan_type claim
    /// instead: an admin-portal plan change does not re-issue the tenant's token, so that claim stays
    /// stale for up to an access-token lifetime (a downgraded tenant would keep Pro access meanwhile).
    /// </summary>
    public static Task<PlanType?> TierAsync(IAppDbContext db, Guid tenantId, CancellationToken ct)
        => db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => (PlanType?)t.Plan!.PlanType)
            .FirstOrDefaultAsync(ct);

    private static Task<PlanFeatureSet?> PlanOf(IAppDbContext db, Guid tenantId, CancellationToken ct)
        => db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => new PlanFeatureSet(t.Plan!.Name, t.Plan.CustomRolesEnabled, t.Plan.WhatsappEnabled))
            .FirstOrDefaultAsync(ct);

    private sealed record PlanFeatureSet(string Name, bool CustomRolesEnabled, bool WhatsappEnabled);
}
