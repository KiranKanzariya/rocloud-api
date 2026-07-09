using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Entities.Platform;
using ROCloud.Domain.Enums;

namespace ROCloud.API.Middleware;

/// <summary>
/// Resolves the current tenant (X-Tenant header → subdomain → JWT tenant_id claim),
/// validates its status, and populates the scoped ITenantContext. Runs AFTER
/// authentication so the JWT claim is available. Auth/health/swagger paths are skipped.
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

        var tenant = await ResolveTenantAsync(context, db);
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

    private static async Task<Tenant?> ResolveTenantAsync(HttpContext context, IAppDbContext db)
    {
        // 1. X-Tenant header (subdomain sent by the portal)
        var headerSubdomain = context.Request.Headers["X-Tenant"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(headerSubdomain))
            return await db.Tenants.Include(t => t.Plan)
                .FirstOrDefaultAsync(t => t.Subdomain == headerSubdomain);

        // 2. Subdomain extracted from the request host
        var label = context.Request.Host.Host.Split('.').FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(label) &&
            !ReservedHostLabels.Contains(label, StringComparer.OrdinalIgnoreCase))
        {
            var bySubdomain = await db.Tenants.Include(t => t.Plan)
                .FirstOrDefaultAsync(t => t.Subdomain == label);
            if (bySubdomain is not null) return bySubdomain;
        }

        // 3. JWT tenant_id claim
        if (Guid.TryParse(context.User.FindFirst("tenant_id")?.Value, out var tenantId))
            return await db.Tenants.Include(t => t.Plan)
                .FirstOrDefaultAsync(t => t.Id == tenantId);

        return null;
    }
}
