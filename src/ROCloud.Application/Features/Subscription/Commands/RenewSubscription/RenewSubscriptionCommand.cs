using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Settings;
using ROCloud.Application.Features.Subscription.Dtos;
using ROCloud.Application.Features.Subscription.Services;
using ROCloud.Domain.Enums;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Features.Subscription.Commands.RenewSubscription;

/// <summary>
/// On-demand renewal (guide §25): raises — or returns the existing open — Pending renewal invoice for
/// the current tenant's SAME plan (Option A), so the owner can renew from the portal even if the daily
/// SubscriptionExpiryJob hasn't run. For a fully-discounted / free plan (net ₹0) there is nothing to
/// pay, so it AUTO-RENEWS instead (₹0 Paid invoice + term rolled forward + reactivated), mirroring the
/// job. Idempotent: never creates a second open invoice. Returns the invoice — Pending (to pay via the
/// pay-invoice flow) or Paid (free renewal already applied).
/// </summary>
public sealed record RenewSubscriptionCommand : IRequest<SubscriptionInvoiceDto>;

public class RenewSubscriptionCommandHandler : IRequestHandler<RenewSubscriptionCommand, SubscriptionInvoiceDto>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAppSettings _settings;
    private readonly ISubscriptionInvoiceDelivery _delivery;

    public RenewSubscriptionCommandHandler(
        IAppDbContext db, ITenantContext tenant, IAppSettings settings, ISubscriptionInvoiceDelivery delivery)
    {
        _db = db;
        _tenant = tenant;
        _settings = settings;
        _delivery = delivery;
    }

    public async Task<SubscriptionInvoiceDto> Handle(RenewSubscriptionCommand request, CancellationToken ct)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters().Include(t => t.Plan)
            .FirstOrDefaultAsync(t => t.Id == _tenant.TenantId, ct)
            ?? throw new NotFoundException("Tenant", _tenant.TenantId);
        var plan = tenant.Plan ?? throw new NotFoundException("Plan", tenant.PlanId);

        // Idempotent: if an open invoice already exists, return it rather than creating another.
        var existing = await _db.SubscriptionInvoices
            .Where(i => i.TenantId == tenant.Id && i.Status == SubscriptionInvoiceStatus.Pending)
            .OrderByDescending(i => i.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (existing is not null)
            return Map(existing);

        var now = DateTime.UtcNow;
        if (tenant.Status == TenantStatus.Cancelled)
            throw Invalid("Your subscription is cancelled. Please subscribe to reactivate.");
        if (tenant.SubscriptionEndsAt is not { } end)
            throw Invalid("You don't have a subscription to renew yet.");
        if (end > now.AddDays(_settings.SubscriptionInvoiceLeadDays))
            throw Invalid("Your subscription isn't due for renewal yet.");

        var yearly = string.Equals(
            await SubscriptionInvoiceFactory.LatestBillingCycleAsync(_db, tenant.Id, ct),
            "Yearly", StringComparison.OrdinalIgnoreCase);
        var cycle = yearly ? "Yearly" : "Monthly";
        var unit = yearly ? "year" : "month";
        var gross = yearly ? plan.YearlyPrice : plan.MonthlyPrice;
        var net = SubscriptionDiscountCalculator.Net(tenant.SubscriptionDiscountType, tenant.SubscriptionDiscountValue, gross);

        if (net <= 0m)
        {
            // Free / fully-discounted → nothing to pay: auto-renew now (mirrors the expiry job). Record
            // a ₹0 Paid invoice, roll the term forward one cycle, and reactivate — no payment needed.
            var freeBasis = end > now ? end : now;
            var freeInvoice = await SubscriptionInvoiceFactory.BuildAsync(
                _db, tenant, plan, cycle, DateOnly.FromDateTime(freeBasis), SubscriptionInvoiceStatus.Paid,
                $"{plan.Name} plan — 1 {unit} (free renewal)", ct);
            _db.SubscriptionInvoices.Add(freeInvoice);   // no email; its PDF renders on demand
            tenant.SubscriptionEndsAt = yearly ? freeBasis.AddYears(1) : freeBasis.AddMonths(1);
            tenant.Status = TenantStatus.Active;
            await _db.SaveChangesAsync(ct);
            return Map(freeInvoice);
        }

        // Payable → raise a Pending invoice for the owner to pay.
        var invoice = await SubscriptionInvoiceFactory.BuildAsync(
            _db, tenant, plan, cycle, DateOnly.FromDateTime(end), SubscriptionInvoiceStatus.Pending,
            $"{plan.Name} plan — 1 {unit} renewal", ct);
        _db.SubscriptionInvoices.Add(invoice);

        // Email the owner the invoice (best-effort) — same as the job path.
        await _delivery.IssueAsync(invoice, tenant, ct);

        await _db.SaveChangesAsync(ct);
        return Map(invoice);
    }

    private static ValidationException Invalid(string message) =>
        new(new Dictionary<string, string[]> { ["subscription"] = [message] });

    private static SubscriptionInvoiceDto Map(Domain.Entities.Platform.SubscriptionInvoice i) => new(
        i.Id, i.InvoiceNumber, i.PlanType, i.BillingCycle,
        i.PeriodStart, i.PeriodEnd, i.GrossAmount, i.DiscountAmount, i.Amount,
        i.Status, i.DueDate, i.Description, i.PaidAt);
}
