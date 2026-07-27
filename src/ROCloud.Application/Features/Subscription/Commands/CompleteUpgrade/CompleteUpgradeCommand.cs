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
        var now = DateTime.UtcNow;
        var gross = yearly ? plan.YearlyPrice : plan.MonthlyPrice;
        var fullCycleNet = SubscriptionDiscountCalculator.Net(
            tenant.SubscriptionDiscountType, tenant.SubscriptionDiscountValue, gross);

        // What the tenant pays for their CURRENT plan on this cycle — the baseline a mid-cycle change
        // is prorated against. Null plan (shouldn't happen) is treated as ₹0, i.e. a full-price change.
        var currentPlan = await _db.Plans.FirstOrDefaultAsync(p => p.Id == tenant.PlanId, ct);
        var oldGross = currentPlan is null ? 0m : (yearly ? currentPlan.YearlyPrice : currentPlan.MonthlyPrice);
        var oldNet = currentPlan is null ? 0m : SubscriptionDiscountCalculator.Net(
            tenant.SubscriptionDiscountType, tenant.SubscriptionDiscountValue, oldGross);

        var change = PlanChangeCalculator.Decide(tenant.SubscriptionEndsAt, oldNet, fullCycleNet, yearly, now);

        // Asking for the plan you are already on, mid-term, means one of two things — and never a
        // prorated "change", because there is no price difference to prorate.
        if (change.Kind != PlanChangeKind.NewTerm && currentPlan?.Id == plan.Id)
        {
            // (a) Undo a pending downgrade. Free, and the only way back once one is scheduled.
            if (tenant.ScheduledPlanId is not null)
            {
                tenant.ScheduledPlanId = null;
                await _db.SaveChangesAsync(ct);
                return;
            }

            // (b) An early RENEWAL of the same plan: buy another full cycle stacked on the current end,
            //     so no already-paid day is lost. This is the prepaid-term path, not a plan change.
            change = change with { Kind = PlanChangeKind.NewTerm, Amount = fullCycleNet };
        }

        // A DOWNGRADE mid-cycle is never charged and never refunded: the tenant keeps the plan they
        // already paid for until the period ends, and the cheaper one is parked until then. The renewal
        // job prices the next invoice at the scheduled plan and flips plan_id when the term expires.
        if (change.Kind == PlanChangeKind.Downgrade)
        {
            tenant.ScheduledPlanId = plan.Id;
            await VoidOpenRenewalInvoicesAsync(tenant.Id, ct);
            await _db.SaveChangesAsync(ct);
            return;
        }

        // What to charge now: the prorated difference for an upgrade, a full cycle for a new term,
        // nothing for a lateral move between equally-priced plans.
        var amount = change.Amount;

        // A paid upgrade (net > 0) with live Razorpay must be backed by a VERIFIED order — never
        // trust the client. Free upgrades and the dev/unconfigured path skip this.
        string? paymentId = null;
        string? paymentMethod = null;
        string? paymentInstrument = null;
        if (amount >= PlanChangeCalculator.MinChargeableAmount && _razorpay.IsConfigured)
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
            // Keep the captured payment id — it is what the admin billing screens show, and the
            // only handle for reconciling this charge against Razorpay (refunds, disputes).
            paymentId = status.PaymentId;
            paymentMethod = status.Method;
            paymentInstrument = status.Instrument;
        }

        tenant.PlanId = plan.Id;
        tenant.Status = TenantStatus.Active;
        tenant.TrialEndsAt = null;
        // Moving to a plan clears any downgrade that was waiting for period end — the owner changed
        // their mind, and leaving it armed would silently undo what they just paid for.
        tenant.ScheduledPlanId = null;

        DateTime termStart, termEnd;
        if (change.Kind == PlanChangeKind.NewTerm)
        {
            // No live term — this buys one. One cycle of USABLE access: extends from the current end
            // when renewing early (no paid day lost), bills the grace days a lapsed tenant used, and
            // credits any locked-out days back.
            termStart = SubscriptionTermCalculator.TermStart(tenant.SubscriptionEndsAt, now);
            termEnd = SubscriptionTermCalculator.NextEnd(
                tenant.SubscriptionEndsAt, yearly, _settings.SubscriptionOverdueGraceDays, now);
            tenant.SubscriptionEndsAt = termEnd;
        }
        else
        {
            // Mid-cycle upgrade (or lateral move): the renewal date does NOT move. They bought a better
            // plan for the days they had already paid for — not more days. The invoice states exactly
            // that window, so "₹541.94" is legible against "12 days of Pro instead of Basic".
            termStart = now;
            termEnd = tenant.SubscriptionEndsAt!.Value;
        }

        // Record the platform billing transaction (feeds the super-admin billing dashboard, guide §26).
        // Linked to its invoice below, once that invoice exists.
        var transaction = new Domain.Entities.Platform.PlatformBillingTransaction
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            PlanType = plan.PlanType.ToString(),
            Amount = amount,
            BillingCycle = yearly ? "Yearly" : "Monthly",
            Status = "Paid",
            RazorpayPaymentId = paymentId,
            PaymentMethod = paymentMethod,
            PaymentInstrument = paymentInstrument
        };
        _db.PlatformBillingTransactions.Add(transaction);

        // Supersede any open Pending renewal invoice. For a new term it would double-bill the period we
        // just covered; for a mid-cycle change it is priced at the plan they have just left. Either way
        // the daily renewal job re-raises a correct one at lead time.
        await VoidOpenRenewalInvoicesAsync(tenant.Id, ct);

        // The Paid invoice for the owner's billing history. A new term is the full plan price for one
        // cycle (Option A); a mid-cycle upgrade is the prorated DIFFERENCE for the days remaining.
        var billingCycle = yearly ? "Yearly" : "Monthly";
        var unit = yearly ? "year" : "month";
        var description = change.Kind == PlanChangeKind.NewTerm
            ? $"{plan.Name} plan — 1 {unit}"
            : $"{plan.Name} plan — upgrade for {change.RemainingDays} of {change.CycleDays} days";

        var paidInvoice = await SubscriptionInvoiceFactory.BuildAsync(
            _db, tenant, plan, billingCycle, DateOnly.FromDateTime(termStart),
            SubscriptionInvoiceStatus.Paid, description, ct,
            periodEnd: DateOnly.FromDateTime(termEnd));

        if (change.Kind != PlanChangeKind.NewTerm)
        {
            // The factory prices a whole cycle of the target plan. A mid-cycle change bills only the
            // prorated difference, so restate the money to what was actually charged — keeping
            // Subtotal − Discount = Total, so the document still adds up.
            var grossDelta = PlanChangeCalculator.Prorate(
                Math.Max(0m, gross - oldGross), change.RemainingDays, change.CycleDays);
            paidInvoice.GrossAmount = grossDelta;
            paidInvoice.Amount = amount;
            paidInvoice.DiscountAmount = Math.Max(0m, grossDelta - amount);
        }
        paidInvoice.RazorpayOrderId = request.OrderId;
        paidInvoice.RazorpayPaymentId = paymentId;
        paidInvoice.PaymentMethod = paymentMethod;
        paidInvoice.PaymentInstrument = paymentInstrument;
        _db.SubscriptionInvoices.Add(paidInvoice);
        transaction.SubscriptionInvoiceId = paidInvoice.Id;   // link the ledger row to this invoice

        // Store the PDF (sets PdfUrl) and email the owner a receipt (best-effort — never blocks the upgrade).
        await _invoiceDelivery.ReceiptAsync(paidInvoice, tenant, ct);

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Voids the tenant's open Pending renewal invoices. Called whenever the plan or the term changes
    /// underneath them: a stale Pending invoice either double-bills a period now covered, or quotes the
    /// price of a plan the tenant no longer has. SubscriptionExpiryJob re-raises a correct one.
    /// </summary>
    private async Task VoidOpenRenewalInvoicesAsync(Guid tenantId, CancellationToken ct)
    {
        var open = await _db.SubscriptionInvoices
            .Where(i => i.TenantId == tenantId && i.Status == SubscriptionInvoiceStatus.Pending)
            .ToListAsync(ct);
        foreach (var invoice in open)
            invoice.Status = SubscriptionInvoiceStatus.Void;
    }
}
