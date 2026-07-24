using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Settings;
using ROCloud.Application.Features.Subscription.Services;
using ROCloud.Domain.Enums;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Features.Subscription.Commands.CompleteUpgrade;

/// <summary>
/// Applies a plan change to the tenant after payment. The new plan_type takes effect on the next
/// token refresh (the refresh handler reads tenant.Plan from the DB).
///
/// SECURITY (guide §25): a paid upgrade is only applied after the Razorpay order is verified paid
/// SERVER-SIDE (not trusting the client), so even the Owner can't change plan without paying.
/// Free upgrades (₹0 net) and the dev/unconfigured path skip payment. <see cref="OrderId"/> is the
/// Razorpay order created by InitiateSubscription.
/// </summary>
public sealed record CompleteUpgradeCommand(string PlanType, string BillingCycle = "Monthly", string? OrderId = null) : IRequest;

public class CompleteUpgradeCommandValidator : AbstractValidator<CompleteUpgradeCommand>
{
    public CompleteUpgradeCommandValidator()
    {
        RuleFor(c => c.PlanType)
            .Must(v => Enum.TryParse<PlanType>(v, out _))
            .WithMessage("Invalid plan type.");
        RuleFor(c => c.BillingCycle)
            .Must(v => v is "Monthly" or "Yearly")
            .WithMessage("Billing cycle must be Monthly or Yearly.");
    }
}

public class CompleteUpgradeCommandHandler : IRequestHandler<CompleteUpgradeCommand>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IRazorpayService _razorpay;
    private readonly ISubscriptionInvoiceDelivery _invoiceDelivery;
    private readonly IAppSettings _settings;

    public CompleteUpgradeCommandHandler(
        IAppDbContext db, ITenantContext tenant, IRazorpayService razorpay,
        ISubscriptionInvoiceDelivery invoiceDelivery, IAppSettings settings)
    {
        _db = db;
        _tenant = tenant;
        _razorpay = razorpay;
        _invoiceDelivery = invoiceDelivery;
        _settings = settings;
    }

    public async Task Handle(CompleteUpgradeCommand request, CancellationToken ct)
    {
        var planType = Enum.Parse<PlanType>(request.PlanType);
        var plan = await _db.Plans.FirstOrDefaultAsync(p => p.PlanType == planType && p.IsActive, ct)
                   ?? throw new NotFoundException("Plan", request.PlanType);

        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == _tenant.TenantId, ct)
            ?? throw new NotFoundException("Tenant", _tenant.TenantId);

        // A "downgrade" via upgrade-complete must not drop the tenant below its current usage.
        await PlanChangeGuard.EnsureUsageFitsAsync(_db, tenant.Id, plan, ct);

        var yearly = string.Equals(request.BillingCycle, "Yearly", StringComparison.OrdinalIgnoreCase);
        var gross = yearly ? plan.YearlyPrice : plan.MonthlyPrice;

        // Charge the price net of the tenant's standing subscription discount (guide §26).
        var amount = SubscriptionDiscountCalculator.Net(
            tenant.SubscriptionDiscountType, tenant.SubscriptionDiscountValue, gross);

        // A paid upgrade (net > 0) with live Razorpay must be backed by a VERIFIED order — never
        // trust the client. Free upgrades and the dev/unconfigured path skip this.
        if (amount > 0m && _razorpay.IsConfigured)
        {
            if (string.IsNullOrWhiteSpace(request.OrderId))
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["payment"] = ["Payment reference is missing — complete the payment first."]
                });

            var status = await _razorpay.GetOrderPaymentStatusAsync(request.OrderId, ct);
            if (!status.Paid)
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["payment"] = ["Payment could not be verified. Your plan was not changed."]
                });
        }

        tenant.PlanId = plan.Id;
        tenant.Status = TenantStatus.Active;
        tenant.TrialEndsAt = null;
        // One cycle of USABLE access: extends from the current end when upgrading early (no paid day
        // lost), bills the grace days a lapsed tenant used, and credits any locked-out days back.
        var now = DateTime.UtcNow;
        var termStart = SubscriptionTermCalculator.TermStart(tenant.SubscriptionEndsAt, now);
        var termEnd = SubscriptionTermCalculator.NextEnd(
            tenant.SubscriptionEndsAt, yearly, _settings.SubscriptionOverdueGraceDays, now);
        tenant.SubscriptionEndsAt = termEnd;

        // Record the platform billing transaction (feeds the super-admin billing dashboard, guide §26).
        _db.PlatformBillingTransactions.Add(new Domain.Entities.Platform.PlatformBillingTransaction
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            PlanType = plan.PlanType.ToString(),
            Amount = amount,
            BillingCycle = yearly ? "Yearly" : "Monthly",
            Status = "Paid"
        });

        // This paid upgrade/renewal supersedes any open Pending renewal invoice — Void it so the owner
        // isn't asked to pay twice for the now-covered period (plan §5.6).
        var openInvoices = await _db.SubscriptionInvoices
            .Where(i => i.TenantId == tenant.Id && i.Status == SubscriptionInvoiceStatus.Pending)
            .ToListAsync(ct);
        foreach (var open in openInvoices)
            open.Status = SubscriptionInvoiceStatus.Void;

        // Record the Paid subscription invoice for the owner's billing history (Option A: full plan
        // price, one cycle). The period is the term actually granted, including any credited days.
        var billingCycle = yearly ? "Yearly" : "Monthly";
        var paidInvoice = await SubscriptionInvoiceFactory.BuildAsync(
            _db, tenant, plan, billingCycle, DateOnly.FromDateTime(termStart),
            SubscriptionInvoiceStatus.Paid, $"{plan.Name} plan — 1 {(yearly ? "year" : "month")}", ct,
            periodEnd: DateOnly.FromDateTime(termEnd));
        paidInvoice.RazorpayOrderId = request.OrderId;
        _db.SubscriptionInvoices.Add(paidInvoice);

        // Store the PDF (sets PdfUrl) and email the owner a receipt (best-effort — never blocks the upgrade).
        await _invoiceDelivery.ReceiptAsync(paidInvoice, tenant, ct);

        await _db.SaveChangesAsync(ct);
    }
}
