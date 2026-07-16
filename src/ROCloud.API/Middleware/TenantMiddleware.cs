using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Entities.Platform;
using ROCloud.Domain.Enums;

namespace ROCloud.API.Middleware;

/// <summary>
/// Resolves the current tenant, validates its status, and populates the scoped ITenantContext. Runs
/// AFTER authentication so the JWT is available. For an authenticated caller the JWT <c>tenant_id</c>
/// claim is authoritative — an X-Tenant header / host subdomain may only agree with it, and one that
/// names a different tenant is rejected (403 TENANT_MISMATCH) rather than honoured, so a valid token
/// cannot be used to reach another tenant's data. Unauthenticated requests fall back to header →
/// subdomain. Auth/health/swagger/platform paths are skipped.
/// </summary>
public class TenantMiddleware
{
    private static readonly string[] ExcludedPrefixes =
    {
        "/api/auth/login", "/api/auth/google", "/api/auth/register", "/api/auth/refresh",
        "/api/auth/forgot-password", "/api/auth/reset-password", "/api/auth/find-workspace",
        "/api/health", "/swagger", "/api/files",
        // Platform (super-admin) endpoints are never tenant-scoped (guide §26).
        "/api/platform"
    };

    // Billing self-service (guide §25): an Overdue-past-grace or Suspended owner may still reach these
    // to pay and restore access — otherwise the payment block would be an inescapable dead-end.
    // Includes /api/plans because the renew/upgrade modal must load the plan list to pay.
    private static readonly string[] BillingSelfServicePrefixes = { "/api/subscription", "/api/plans" };

    private static readonly string[] ReservedHostLabels = { "localhost", "api", "admin", "www" };

    private readonly RequestDelegate _next;

    public TenantMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(
        HttpContext context, IAppDbContext db, ITenantContext tenantContext, IConfiguration config)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (ExcludedPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        var (tenant, mismatch) = await ResolveTenantAsync(context, db);

        // An authenticated caller whose JWT names one tenant may not act on another by supplying a
        // different X-Tenant header / host subdomain — that would bypass tenant isolation (the EF query
        // filter and the RLS session GUC both key off the resolved tenant). Reject the attempt.
        if (mismatch)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "Tenant mismatch.", code = "TENANT_MISMATCH" });
            return;
        }

        if (tenant is not null)
        {
            // Blocked tenants may still hit the billing endpoints to pay and restore access (guide §25).
            var isBillingPath = BillingSelfServicePrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));

            // Cancelled: the owner keeps the access they already paid for until the period ends
            // (guide §25). After it ends they're blocked EXCEPT the billing endpoints, so they can
            // log back in and re-subscribe to reclaim their existing workspace (no admin, no re-signup).
            if (tenant.Status is TenantStatus.Cancelled)
            {
                var paidUntil = tenant.SubscriptionEndsAt ?? tenant.TrialEndsAt;
                var periodEnded = paidUntil is null || paidUntil < DateTime.UtcNow;
                if (periodEnded && !isBillingPath)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsJsonAsync(new { error = "This account has been cancelled.", code = "TENANT_CANCELLED" });
                    return;
                }
                // Within the period, or a billing request after it → fall through (access / pay).
            }

            // Suspended: blocked everywhere except the billing endpoints, so the owner can pay to reactivate.
            if (tenant.Status is TenantStatus.Suspended && !isBillingPath)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "This account is suspended for non-payment.", code = "TENANT_SUSPENDED" });
                return;
            }

            // Overdue beyond the configured grace period (Subscription:OverdueGraceDays) → Payment Required,
            // again except the billing endpoints so the renewal flow itself stays reachable.
            // A tenant that lapsed straight from trial has no paid end date — give it the (shorter)
            // trial grace; a lapsed paid subscription gets the standard overdue grace.
            var isTrialLapse = tenant.SubscriptionEndsAt is null && tenant.TrialEndsAt is not null;
            var graceDays = isTrialLapse
                ? (int.TryParse(config["Subscription:TrialGraceDays"], out var tg) ? tg : 2)
                : (int.TryParse(config["Subscription:OverdueGraceDays"], out var g) ? g : 7);
            // Effective end date: paid end, or trial end for a tenant that lapsed straight from trial.
            var effectiveEnd = tenant.SubscriptionEndsAt ?? tenant.TrialEndsAt;
            if (!isBillingPath
                && tenant.Status == TenantStatus.Overdue
                && effectiveEnd is { } endsAt
                && endsAt < DateTime.UtcNow.AddDays(-graceDays))
            {
                context.Response.StatusCode = StatusCodes.Status402PaymentRequired;
                await context.Response.WriteAsJsonAsync(new { error = "Subscription payment is overdue.", code = "PAYMENT_REQUIRED" });
                return;
            }

            tenantContext.TenantId = tenant.Id;
            tenantContext.Subdomain = tenant.Subdomain;
            tenantContext.PlanType = tenant.Plan?.PlanType.ToString() ?? string.Empty;
            tenantContext.LanguageCode = tenant.DefaultLanguage;
        }

        await _next(context);
    }

    /// <summary>
    /// Resolves the request's tenant. For an authenticated caller the JWT <c>tenant_id</c> claim is the
    /// source of truth: an X-Tenant header / host subdomain may only AGREE with it, never override it.
    /// A header/subdomain that resolves to a different tenant returns <c>Mismatch = true</c> so the
    /// caller is rejected (isolation-bypass attempt). Unauthenticated tenant-scoped requests (which carry
    /// no claim to protect) still resolve purely from header → subdomain, as before.
    /// </summary>
    private static async Task<(Tenant? Tenant, bool Mismatch)> ResolveTenantAsync(
        HttpContext context, IAppDbContext db)
    {
        Guid? claimTenantId = Guid.TryParse(context.User.FindFirst("tenant_id")?.Value, out var cid)
            ? cid
            : null;

        // Tenant the request ASKS for via header/subdomain (may be null if neither is supplied/known).
        var requested = await ResolveRequestedTenantAsync(context, db);

        if (claimTenantId is { } tid)
        {
            // Authenticated: the claim wins. A header/subdomain naming a *different* tenant is rejected;
            // one that agrees (or is absent) is fine. Fall back to the claimed tenant when none was asked.
            if (requested is not null && requested.Id != tid)
                return (null, true);

            var tenant = requested ?? await db.Tenants.AsNoTracking().Include(t => t.Plan)
                .FirstOrDefaultAsync(t => t.Id == tid);
            return (tenant, false);
        }

        // Unauthenticated: no claim to protect — trust header/subdomain (login/register/etc. are excluded).
        return (requested, false);
    }

    /// <summary>Resolves the tenant requested by the X-Tenant header, then the host subdomain.</summary>
    private static async Task<Tenant?> ResolveRequestedTenantAsync(HttpContext context, IAppDbContext db)
    {
        // 1. X-Tenant header (subdomain sent by the portal). Read-only: the middleware only inspects the
        // tenant to set the request's ITenantContext, so it is never tracked.
        var headerSubdomain = context.Request.Headers["X-Tenant"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(headerSubdomain))
            return await db.Tenants.AsNoTracking().Include(t => t.Plan)
                .FirstOrDefaultAsync(t => t.Subdomain == headerSubdomain);

        // 2. Subdomain extracted from the request host
        var label = context.Request.Host.Host.Split('.').FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(label) &&
            !ReservedHostLabels.Contains(label, StringComparer.OrdinalIgnoreCase))
        {
            var bySubdomain = await db.Tenants.AsNoTracking().Include(t => t.Plan)
                .FirstOrDefaultAsync(t => t.Subdomain == label);
            if (bySubdomain is not null) return bySubdomain;
        }

        return null;
    }
}
