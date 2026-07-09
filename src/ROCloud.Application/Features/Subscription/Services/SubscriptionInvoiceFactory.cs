using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Entities.Platform;

namespace ROCloud.Application.Features.Subscription.Services;

/// <summary>Subscription-invoice status values (also the DB CHECK constraint on subscription_invoices).</summary>
public static class SubscriptionInvoiceStatus
{
    public const string Pending = "Pending";
    public const string Paid = "Paid";
    public const string Void = "Void";
}

/// <summary>
/// Builds a <see cref="SubscriptionInvoice"/> for a tenant's ROCloud plan bill (Option A — no
/// proration): full plan price for one cycle, net of the standing subscription discount. Assigns the
/// human-friendly number and the period. Used by the renewal job and the upgrade/first-purchase flows.
/// </summary>
public static class SubscriptionInvoiceFactory
{
    public static async Task<SubscriptionInvoice> BuildAsync(
        IAppDbContext db, Tenant tenant, Plan plan, string billingCycle,
        DateOnly periodStart, string status, string? description, CancellationToken ct)
    {
        var yearly = string.Equals(billingCycle, "Yearly", StringComparison.OrdinalIgnoreCase);
        var gross = yearly ? plan.YearlyPrice : plan.MonthlyPrice;
        var discount = SubscriptionDiscountCalculator.Discount(
            tenant.SubscriptionDiscountType, tenant.SubscriptionDiscountValue, gross);
        var amount = Math.Max(0m, gross - discount);
        var periodEnd = yearly ? periodStart.AddYears(1) : periodStart.AddMonths(1);

        return new SubscriptionInvoice
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            InvoiceNumber = await NextInvoiceNumberAsync(db, ct),
            PlanType = plan.PlanType.ToString(),
            BillingCycle = yearly ? "Yearly" : "Monthly",
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            GrossAmount = gross,
            DiscountAmount = discount,
            Amount = amount,
            Status = status,
            DueDate = periodStart,
            Description = description,
            PaidAt = status == SubscriptionInvoiceStatus.Paid ? DateTime.UtcNow : null,
        };
    }

    /// <summary>The tenant's current billing cycle, inferred from the latest paid transaction (default Monthly).</summary>
    public static async Task<string> LatestBillingCycleAsync(IAppDbContext db, Guid tenantId, CancellationToken ct)
    {
        var last = await db.PlatformBillingTransactions
            .Where(t => t.TenantId == tenantId && t.Status == SubscriptionInvoiceStatus.Paid)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => t.BillingCycle)
            .FirstOrDefaultAsync(ct);
        return string.Equals(last, "Yearly", StringComparison.OrdinalIgnoreCase) ? "Yearly" : "Monthly";
    }

    /// <summary>Next <c>SUB-{year}-{seq}</c> number. Zero-padded so lexical order == numeric within a year.</summary>
    private static async Task<string> NextInvoiceNumberAsync(IAppDbContext db, CancellationToken ct)
    {
        var prefix = $"SUB-{DateTime.UtcNow.Year}-";
        var last = await db.SubscriptionInvoices
            .Where(i => i.InvoiceNumber.StartsWith(prefix))
            .OrderByDescending(i => i.InvoiceNumber)
            .Select(i => i.InvoiceNumber)
            .FirstOrDefaultAsync(ct);

        var seq = 1;
        if (last is not null && int.TryParse(last[prefix.Length..], out var n))
            seq = n + 1;
        return $"{prefix}{seq:D6}";
    }
}
