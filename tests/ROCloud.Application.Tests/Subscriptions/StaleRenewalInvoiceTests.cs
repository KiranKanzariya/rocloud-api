using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Platform.Tenants.Commands.ChangeTenantPlan;
using ROCloud.Application.Features.Platform.Tenants.Commands.GrantFreeMonths;
using ROCloud.Application.Features.Subscription.Commands.PayInvoice;
using ROCloud.Application.Features.Subscription.Services;
using ROCloud.Domain.Entities.Platform;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Tests.Subscriptions;

/// <summary>
/// A Pending renewal invoice is a quote for one period at one plan's price. The platform-admin actions
/// that move either of those — gifting free months, overriding the plan — must cancel it — saying why — exactly as the
/// owner's own plan change already does.
///
/// <para>The damage from leaving it is not merely a confusing document. SubscriptionExpiryJob skips any
/// tenant that already has an open Pending invoice, so a stale one starves the tenant of the NEXT
/// invoice: they are never billed, never emailed, and lapse to Overdue and then Suspended having done
/// nothing wrong. That is the case these tests exist to prevent.</para>
/// </summary>
public class StaleRenewalInvoiceTests
{
    private static (AppDbContext Db, TenantContext Ctx) NewDb()
    {
        var ctx = new TenantContext { TenantId = Guid.NewGuid() };
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"stale-{Guid.NewGuid()}").Options, ctx);
        return (db, ctx);
    }

    private static async Task<(Plan Basic, Plan Pro)> SeedAsync(
        AppDbContext db, Guid tenantId, DateTime endsAt, TenantStatus status = TenantStatus.Active)
    {
        var basic = new Plan { Id = Guid.NewGuid(), Name = "Basic", PlanType = PlanType.Basic, MonthlyPrice = 1099m, YearlyPrice = 10990m, IsActive = true, MaxCustomers = 500 };
        var pro = new Plan { Id = Guid.NewGuid(), Name = "Pro", PlanType = PlanType.Pro, MonthlyPrice = 2499m, YearlyPrice = 24990m, IsActive = true, MaxCustomers = 5000 };
        db.Plans.AddRange(basic, pro);
        db.Tenants.Add(new Tenant
        {
            Id = tenantId, PlanId = basic.Id, Name = "Sharma RO", Subdomain = "sharma",
            OwnerName = "O", OwnerEmail = "o@x.com", OwnerMobile = "9",
            Status = status, SubscriptionEndsAt = endsAt,
        });
        await db.SaveChangesAsync();
        return (basic, pro);
    }

    private static SubscriptionInvoice PendingInvoice(Guid tenantId, decimal amount = 1099m) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, InvoiceNumber = $"SUB-{Guid.NewGuid():N}"[..20],
        PlanType = "Basic", BillingCycle = "Monthly",
        PeriodStart = DateOnly.FromDateTime(DateTime.UtcNow), PeriodEnd = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
        GrossAmount = amount, DiscountAmount = 0m, Amount = amount,
        Status = SubscriptionInvoiceStatus.Pending, DueDate = DateOnly.FromDateTime(DateTime.UtcNow),
    };

    // ---- Free months -------------------------------------------------------------------------

    [Fact]
    public async Task GrantingFreeMonths_CancelsTheOpenInvoice_WithAReason()
    {
        // The gifted month covers the very period that invoice bills. Charging for it would make the
        // gift meaningless — the owner would see a free month and a bill for it side by side.
        var (db, ctx) = NewDb();
        await SeedAsync(db, ctx.TenantId, DateTime.UtcNow.AddDays(4));
        db.SubscriptionInvoices.Add(PendingInvoice(ctx.TenantId));
        await db.SaveChangesAsync();

        await new GrantFreeMonthsCommandHandler(db)
            .Handle(new GrantFreeMonthsCommand(ctx.TenantId, 1), CancellationToken.None);

        var invoice = await db.SubscriptionInvoices.FirstAsync();
        Assert.Equal(SubscriptionInvoiceStatus.Cancelled, invoice.Status);
        // The owner already has this invoice in their inbox. Without a reason on it, a bill that
        // silently turns "cancelled" reads as a billing fault rather than the gift it was.
        Assert.Equal("1 free month granted by ROCloud, covering this period. Nothing to pay.",
            invoice.CancellationReason);
    }

    [Fact]
    public async Task TheReasonCountsTheMonthsGranted()
    {
        var (db, ctx) = NewDb();
        await SeedAsync(db, ctx.TenantId, DateTime.UtcNow.AddDays(4));
        db.SubscriptionInvoices.Add(PendingInvoice(ctx.TenantId));
        await db.SaveChangesAsync();

        await new GrantFreeMonthsCommandHandler(db)
            .Handle(new GrantFreeMonthsCommand(ctx.TenantId, 3), CancellationToken.None);

        Assert.StartsWith("3 free months granted", (await db.SubscriptionInvoices.FirstAsync()).CancellationReason);
    }

    [Fact]
    public async Task GrantingFreeMonths_StillExtendsTheTerm_AndReactivates()
    {
        // The cancellation must not cost the gift anything it did before.
        var (db, ctx) = NewDb();
        var endsAt = DateTime.UtcNow.AddDays(4);
        await SeedAsync(db, ctx.TenantId, endsAt, TenantStatus.Overdue);
        db.SubscriptionInvoices.Add(PendingInvoice(ctx.TenantId));
        await db.SaveChangesAsync();

        var newEnd = await new GrantFreeMonthsCommandHandler(db)
            .Handle(new GrantFreeMonthsCommand(ctx.TenantId, 2), CancellationToken.None);

        var tenant = await db.Tenants.FirstAsync();
        Assert.Equal(endsAt.AddMonths(2), newEnd, TimeSpan.FromSeconds(1));
        Assert.Equal(TenantStatus.Active, tenant.Status);
    }

    [Fact]
    public async Task GrantingFreeMonths_LeavesPaidHistoryAlone()
    {
        // Only OPEN invoices are stale. A paid one is a record of money that changed hands.
        var (db, ctx) = NewDb();
        await SeedAsync(db, ctx.TenantId, DateTime.UtcNow.AddDays(4));
        var paid = PendingInvoice(ctx.TenantId);
        paid.Status = SubscriptionInvoiceStatus.Paid;
        db.SubscriptionInvoices.Add(paid);
        await db.SaveChangesAsync();

        await new GrantFreeMonthsCommandHandler(db)
            .Handle(new GrantFreeMonthsCommand(ctx.TenantId, 1), CancellationToken.None);

        Assert.Equal(SubscriptionInvoiceStatus.Paid, (await db.SubscriptionInvoices.FirstAsync()).Status);
    }

    [Fact]
    public async Task ALapsedTenantCanStillBeGifted()
    {
        // The reason the fix cancels rather than refuses. An unpaid invoice stays open indefinitely, so
        // "refuse while an invoice is open" would permanently bar comping exactly the tenants — lapsed,
        // overdue, wavering — a goodwill month is meant to win back.
        var (db, ctx) = NewDb();
        await SeedAsync(db, ctx.TenantId, DateTime.UtcNow.AddDays(-40), TenantStatus.Overdue);
        db.SubscriptionInvoices.Add(PendingInvoice(ctx.TenantId));
        await db.SaveChangesAsync();

        await new GrantFreeMonthsCommandHandler(db)
            .Handle(new GrantFreeMonthsCommand(ctx.TenantId, 1), CancellationToken.None);

        Assert.Equal(TenantStatus.Active, (await db.Tenants.FirstAsync()).Status);
        Assert.Equal(SubscriptionInvoiceStatus.Cancelled, (await db.SubscriptionInvoices.FirstAsync()).Status);
    }

    // ---- Admin plan override -----------------------------------------------------------------

    [Fact]
    public async Task AdminChangingThePlan_CancelsTheOpenInvoice_WithAReason()
    {
        // The invoice quotes Basic. The tenant is now on Pro, and the period it bills will be served on
        // Pro — so it states the wrong plan AND the wrong price.
        var (db, ctx) = NewDb();
        await SeedAsync(db, ctx.TenantId, DateTime.UtcNow.AddDays(4));
        db.SubscriptionInvoices.Add(PendingInvoice(ctx.TenantId));
        await db.SaveChangesAsync();

        await new ChangeTenantPlanCommandHandler(db)
            .Handle(new ChangeTenantPlanCommand(ctx.TenantId, nameof(PlanType.Pro)), CancellationToken.None);

        var tenant = await db.Tenants.FirstAsync();
        var pro = await db.Plans.FirstAsync(p => p.PlanType == PlanType.Pro);
        Assert.Equal(pro.Id, tenant.PlanId);

        var invoice = await db.SubscriptionInvoices.FirstAsync();
        Assert.Equal(SubscriptionInvoiceStatus.Cancelled, invoice.Status);
        Assert.Contains("Pro", invoice.CancellationReason);
    }

    // ---- The payment race --------------------------------------------------------------------

    [Fact]
    public async Task AVerifiedPaymentIsHonoured_EvenIfTheInvoiceWasCancelledMidPayment()
    {
        // The owner is on the Razorpay screen when an admin gifts a month. Razorpay captures the money
        // regardless. Refusing the completion would keep the charge and hand back nothing, leaving a
        // manual refund as the only remedy — so a verified payment is honoured whatever became of the
        // invoice meanwhile.
        var (db, ctx) = NewDb();
        await SeedAsync(db, ctx.TenantId, DateTime.UtcNow.AddDays(4), TenantStatus.Overdue);
        var invoice = PendingInvoice(ctx.TenantId);
        invoice.Status = SubscriptionInvoiceStatus.Cancelled;      // cancelled while the payment was in flight
        invoice.CancellationReason = "1 free month granted by ROCloud, covering this period. Nothing to pay.";
        db.SubscriptionInvoices.Add(invoice);
        await db.SaveChangesAsync();

        var razorpay = new FakeRazorpayService { Configured = true };
        razorpay.PaidStatuses["order_live"] = new RazorpayPaymentStatus(true, "pay_1", "upi", "kk@okaxis");

        await new PayInvoiceCompleteCommandHandler(
                db, ctx, razorpay, new NoOpSubscriptionInvoiceDelivery(), new Auth.FakeAppSettings())
            .Handle(new PayInvoiceCompleteCommand(invoice.Id, "order_live"), CancellationToken.None);

        var settled = await db.SubscriptionInvoices.FirstAsync();
        Assert.Equal(SubscriptionInvoiceStatus.Paid, settled.Status);
        Assert.Equal("pay_1", settled.RazorpayPaymentId);
        Assert.Equal(TenantStatus.Active, (await db.Tenants.FirstAsync()).Status);
        // The cancellation did not hold. A PAID invoice still explaining why it was withdrawn would
        // contradict itself on the owner's own document.
        Assert.Null(settled.CancellationReason);
    }

    [Fact]
    public async Task ACancelledInvoiceCannotBePaid_WithoutAVerifiedPayment()
    {
        // No captured money means nothing to protect, and a cancelled bill must not be resurrected —
        // including in dev, where Razorpay is unconfigured and verification is skipped entirely.
        var (db, ctx) = NewDb();
        await SeedAsync(db, ctx.TenantId, DateTime.UtcNow.AddDays(4));
        var invoice = PendingInvoice(ctx.TenantId);
        invoice.Status = SubscriptionInvoiceStatus.Cancelled;
        db.SubscriptionInvoices.Add(invoice);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            new PayInvoiceCompleteCommandHandler(
                    db, ctx, new FakeRazorpayService(), new NoOpSubscriptionInvoiceDelivery(), new Auth.FakeAppSettings())
                .Handle(new PayInvoiceCompleteCommand(invoice.Id), CancellationToken.None));

        Assert.Equal(SubscriptionInvoiceStatus.Cancelled, (await db.SubscriptionInvoices.FirstAsync()).Status);
    }

    [Fact]
    public async Task APaidInvoiceCannotBePaidTwice()
    {
        var (db, ctx) = NewDb();
        await SeedAsync(db, ctx.TenantId, DateTime.UtcNow.AddDays(4));
        var invoice = PendingInvoice(ctx.TenantId);
        invoice.Status = SubscriptionInvoiceStatus.Paid;
        db.SubscriptionInvoices.Add(invoice);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            new PayInvoiceCompleteCommandHandler(
                    db, ctx, new FakeRazorpayService(), new NoOpSubscriptionInvoiceDelivery(), new Auth.FakeAppSettings())
                .Handle(new PayInvoiceCompleteCommand(invoice.Id), CancellationToken.None));
    }
}
