using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Subscription.Dtos;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Subscription.Commands.InitiateSubscription;

/// <summary>
/// Begins a plan change. In production this would create a Razorpay subscription and return its id
/// for the client to open Checkout. With placeholder keys it returns DevMode=true so the client
/// simulates a successful payment and calls upgrade-complete.
/// </summary>
public sealed record InitiateSubscriptionCommand(string PlanType, string BillingCycle)
    : IRequest<SubscriptionInitiateDto>;

public class InitiateSubscriptionCommandValidator : AbstractValidator<InitiateSubscriptionCommand>
{
    public InitiateSubscriptionCommandValidator()
    {
        RuleFor(c => c.PlanType)
            .Must(v => Enum.TryParse<PlanType>(v, out _))
            .WithMessage("Invalid plan type.");
        RuleFor(c => c.BillingCycle)
            .Must(v => v is "Monthly" or "Yearly")
            .WithMessage("Billing cycle must be Monthly or Yearly.");
    }
}

public class InitiateSubscriptionCommandHandler
    : IRequestHandler<InitiateSubscriptionCommand, SubscriptionInitiateDto>
{
    private readonly IAppDbContext _db;
    private readonly IRazorpayService _razorpay;
    private readonly ITenantContext _tenant;

    public InitiateSubscriptionCommandHandler(IAppDbContext db, IRazorpayService razorpay, ITenantContext tenant)
    {
        _db = db;
        _razorpay = razorpay;
        _tenant = tenant;
    }

    public async Task<SubscriptionInitiateDto> Handle(InitiateSubscriptionCommand request, CancellationToken ct)
    {
        var planType = Enum.Parse<PlanType>(request.PlanType);
        var plan = await _db.Plans.FirstOrDefaultAsync(p => p.PlanType == planType && p.IsActive, ct)
                   ?? throw new NotFoundException("Plan", request.PlanType);

        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == _tenant.TenantId, ct)
            ?? throw new NotFoundException("Tenant", _tenant.TenantId);

        // No usable live credentials → dev mode (client simulates the payment, then upgrade-complete).
        var devMode = !_razorpay.IsConfigured;

        var yearly = string.Equals(request.BillingCycle, "Yearly", StringComparison.OrdinalIgnoreCase);
        var gross = yearly ? plan.YearlyPrice : plan.MonthlyPrice;

        // Apply the tenant's standing subscription discount (guide §26).
        var discount = SubscriptionDiscountCalculator.Discount(
            tenant.SubscriptionDiscountType, tenant.SubscriptionDiscountValue, gross);
        var fullCycleNet = gross - discount;

        // What this change really is. Mid-cycle it is prorated over the days left, so the order we
        // create must be for THAT amount — the client opens Checkout with whatever we return here, and
        // CompleteUpgrade re-derives the same figure, so the two must agree.
        var currentPlan = await _db.Plans.FirstOrDefaultAsync(p => p.Id == tenant.PlanId, ct);
        var oldNet = currentPlan is null ? 0m : SubscriptionDiscountCalculator.Net(
            tenant.SubscriptionDiscountType, tenant.SubscriptionDiscountValue,
            yearly ? currentPlan.YearlyPrice : currentPlan.MonthlyPrice);

        var change = PlanChangeCalculator.Decide(
            tenant.SubscriptionEndsAt, oldNet, fullCycleNet, yearly, DateTime.UtcNow);
        var amount = change.Amount;

        // ₹0 to pay — a 100% discount, a downgrade (never charged), or a prorated delta that rounds to
        // nothing. Razorpay rejects zero-amount orders, so the client skips the gateway and completes
        // directly. Sub-₹1 lands here too: Razorpay's minimum order is ₹1.00, and refusing to apply a
        // legitimate plan change over 40 paise would be absurd.
        var isFree = amount < PlanChangeCalculator.MinChargeableAmount;

        // Paid upgrade with live keys → create a real Razorpay order the client must pay before
        // upgrade-complete (which verifies it server-side). One-time order, not a recurring
        // subscription, because there is no auto-renew webhook handling in v1.
        string? orderId = null;
        if (!isFree && !devMode)
        {
            var amountPaise = (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
            // Razorpay caps `receipt` at 40 characters. "sub-" + the 32-char tenant id + "-" + the plan
            // NAME came to 42 for Basic and 47 for Enterprise, so every upgrade to those two tiers was
            // rejected with a 400 (only Pro, at exactly 40, fit). The tier's initial keeps it at 38.
            var receipt = $"sub-{tenant.Id:N}-{planType.ToString()[0]}";
            var order = await _razorpay.CreateOrderAsync(amountPaise, receipt, ct);
            orderId = order.OrderId;
        }

        return new SubscriptionInitiateDto(
            KeyId: _razorpay.PublicKeyId,
            OrderId: orderId,
            PlanType: planType.ToString(),
            Amount: amount,
            Currency: _razorpay.Currency,
            DevMode: devMode,
            GrossAmount: gross,
            DiscountAmount: discount,
            IsFree: isFree,
            ChangeKind: change.Kind.ToString(),
            RemainingDays: change.RemainingDays,
            FullCycleAmount: fullCycleNet,
            EffectiveAt: change.Kind == PlanChangeKind.Downgrade ? tenant.SubscriptionEndsAt : null);
    }
}
