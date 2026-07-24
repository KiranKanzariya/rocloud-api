using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Features.Subscription.Commands.CompleteUpgrade;
using ROCloud.Application.Features.Subscription.Commands.PayInvoice;
using ROCloud.Application.Features.Subscription.Commands.RenewSubscription;
using ROCloud.Application.Features.Subscription.Queries.GetSubscriptionInvoices;
using ROCloud.Application.Features.Subscription.Services;
using ROCloud.Domain.Entities.Platform;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Tests.Subscriptions;

/// <summary>Subscription invoicing (v1, Option A): Paid-invoice + supersede on upgrade, and the
/// pay-invoice flow (verify → mark Paid → extend + reactivate + ledger row).</summary>
public class SubscriptionInvoiceTests
{
    private static (AppDbContext Db, TenantContext Ctx) NewDb()
    {
        var ctx = new TenantContext { TenantId = Guid.NewGuid() };
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"si-{Guid.NewGuid()}").Options, ctx);
        return (db, ctx);
    }

    private static async Task<Plan> SeedAsync(AppDbContext db, Guid tenantId, DateTime? endsAt, TenantStatus status = TenantStatus.Active)
    {
        var plan = new Plan { Id = Guid.NewGuid(), Name = "Pro", PlanType = PlanType.Pro, MonthlyPrice = 999m, YearlyPrice = 9990m, IsActive = true };
        db.Plans.Add(plan);
        db.Tenants.Add(new Tenant
        {
            Id = tenantId, PlanId = plan.Id, Name = "Co", Subdomain = "co",
            OwnerName = "O", OwnerEmail = "o@x.com", OwnerMobile = "9",
            Status = status, SubscriptionEndsAt = endsAt
        });
        await db.SaveChangesAsync();
        return plan;
    }

    private static SubscriptionInvoice PendingInvoice(Guid tenantId, string number, decimal amount = 999m, string cycle = "Monthly") => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, InvoiceNumber = number,
        PlanType = "Pro", BillingCycle = cycle,
        PeriodStart = DateOnly.FromDateTime(DateTime.UtcNow), PeriodEnd = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
        GrossAmount = amount, DiscountAmount = 0m, Amount = amount,
        Status = SubscriptionInvoiceStatus.Pending, DueDate = DateOnly.FromDateTime(DateTime.UtcNow),
    };

    [Fact]
    public async Task Factory_ComputesNetAndNumber()
    {
        var (db, ctx) = NewDb();
        var plan = await SeedAsync(db, ctx.TenantId, DateTime.UtcNow.AddDays(5));
        var tenant = await db.Tenants.FirstAsync();

        var inv = await SubscriptionInvoiceFactory.BuildAsync(
            db, tenant, plan, "Monthly", DateOnly.FromDateTime(DateTime.UtcNow),
            SubscriptionInvoiceStatus.Pending, "Pro plan — 1 month", CancellationToken.None);

        Assert.Equal(999m, inv.GrossAmount);
        Assert.Equal(0m, inv.DiscountAmount);
        Assert.Equal(999m, inv.Amount);
        Assert.StartsWith("SUB-", inv.InvoiceNumber);
    }

    [Fact]
    public async Task CompleteUpgrade_WritesPaidInvoice_AndVoidsOpenPending()
    {
        var (db, ctx) = NewDb();
        await SeedAsync(db, ctx.TenantId, DateTime.UtcNow.AddDays(-1));
        db.SubscriptionInvoices.Add(PendingInvoice(ctx.TenantId, "SEED-1"));
        await db.SaveChangesAsync();

        await new CompleteUpgradeCommandHandler(db, ctx, new FakeRazorpayService(), new NoOpSubscriptionInvoiceDelivery(), new Auth.FakeAppSettings())
            .Handle(new CompleteUpgradeCommand("Pro", "Monthly"), CancellationToken.None);

        var invoices = await db.SubscriptionInvoices.Where(i => i.TenantId == ctx.TenantId).ToListAsync();
        Assert.Contains(invoices, i => i.InvoiceNumber == "SEED-1" && i.Status == SubscriptionInvoiceStatus.Void);
        Assert.Contains(invoices, i => i.Status == SubscriptionInvoiceStatus.Paid && i.PaidAt != null);
    }

    [Fact]
    public async Task PayInvoiceComplete_DevMode_MarksPaid_ExtendsAndReactivates()
    {
        var (db, ctx) = NewDb();
        await SeedAsync(db, ctx.TenantId, DateTime.UtcNow.AddDays(-2), TenantStatus.Overdue);
        var invoice = PendingInvoice(ctx.TenantId, "SEED-1");
        db.SubscriptionInvoices.Add(invoice);
        await db.SaveChangesAsync();

        await new PayInvoiceCompleteCommandHandler(db, ctx, new FakeRazorpayService(), new NoOpSubscriptionInvoiceDelivery(), new Auth.FakeAppSettings())
            .Handle(new PayInvoiceCompleteCommand(invoice.Id), CancellationToken.None);

        var paid = await db.SubscriptionInvoices.FirstAsync(i => i.Id == invoice.Id);
        var tenant = await db.Tenants.FirstAsync(t => t.Id == ctx.TenantId);
        Assert.Equal(SubscriptionInvoiceStatus.Paid, paid.Status);
        Assert.Equal(TenantStatus.Active, tenant.Status);
        Assert.True(tenant.SubscriptionEndsAt > DateTime.UtcNow.AddDays(27));
        Assert.True(await db.PlatformBillingTransactions.AnyAsync(t => t.TenantId == ctx.TenantId && t.Status == "Paid"));
    }

    [Fact]
    public async Task PayInvoiceComplete_LiveKeys_UnpaidOrder_Rejected()
    {
        var (db, ctx) = NewDb();
        await SeedAsync(db, ctx.TenantId, DateTime.UtcNow.AddDays(-2), TenantStatus.Overdue);
        var invoice = PendingInvoice(ctx.TenantId, "SEED-1");
        db.SubscriptionInvoices.Add(invoice);
        await db.SaveChangesAsync();
        var rp = new FakeRazorpayService { Configured = true };   // "order_x" not marked paid

        await Assert.ThrowsAsync<ValidationException>(() =>
            new PayInvoiceCompleteCommandHandler(db, ctx, rp, new NoOpSubscriptionInvoiceDelivery(), new Auth.FakeAppSettings())
                .Handle(new PayInvoiceCompleteCommand(invoice.Id, "order_x"), CancellationToken.None));

        Assert.Equal(SubscriptionInvoiceStatus.Pending,
            (await db.SubscriptionInvoices.FirstAsync(i => i.Id == invoice.Id)).Status);
    }

    [Fact]
    public async Task PayInvoiceInitiate_LiveKeys_CreatesOrder()
    {
        var (db, ctx) = NewDb();
        await SeedAsync(db, ctx.TenantId, DateTime.UtcNow.AddDays(-2), TenantStatus.Overdue);
        var invoice = PendingInvoice(ctx.TenantId, "SEED-1");
        db.SubscriptionInvoices.Add(invoice);
        await db.SaveChangesAsync();
        var rp = new FakeRazorpayService { Configured = true };

        var result = await new PayInvoiceInitiateCommandHandler(db, rp, ctx)
            .Handle(new PayInvoiceInitiateCommand(invoice.Id), CancellationToken.None);

        Assert.False(result.DevMode);
        Assert.Equal("order_test", result.OrderId);
        Assert.Equal("order_test", (await db.SubscriptionInvoices.FirstAsync(i => i.Id == invoice.Id)).RazorpayOrderId);
    }

    [Fact]
    public async Task Renew_Lapsed_CreatesPendingInvoice_Idempotent()
    {
        var (db, ctx) = NewDb();
        await SeedAsync(db, ctx.TenantId, DateTime.UtcNow.AddDays(-2), TenantStatus.Overdue);
        var handler = new RenewSubscriptionCommandHandler(db, ctx, new Auth.FakeAppSettings(), new NoOpSubscriptionInvoiceDelivery());

        var first = await handler.Handle(new RenewSubscriptionCommand(), CancellationToken.None);
        var second = await handler.Handle(new RenewSubscriptionCommand(), CancellationToken.None);

        Assert.Equal(SubscriptionInvoiceStatus.Pending, first.Status);
        Assert.Equal(999m, first.Amount);
        Assert.Equal(first.Id, second.Id);   // idempotent — returns the same open invoice, not a new one
        Assert.Single(await db.SubscriptionInvoices.Where(i => i.TenantId == ctx.TenantId).ToListAsync());
    }

    [Fact]
    public async Task Renew_FreeTenant_AutoRenews_NoPayment()
    {
        var (db, ctx) = NewDb();
        await SeedAsync(db, ctx.TenantId, DateTime.UtcNow.AddDays(-2), TenantStatus.Overdue);
        var t = await db.Tenants.FirstAsync();
        t.SubscriptionDiscountType = SubscriptionDiscountType.Percentage;
        t.SubscriptionDiscountValue = 100m;   // fully discounted → net ₹0
        await db.SaveChangesAsync();

        var result = await new RenewSubscriptionCommandHandler(db, ctx, new Auth.FakeAppSettings(), new NoOpSubscriptionInvoiceDelivery())
            .Handle(new RenewSubscriptionCommand(), CancellationToken.None);

        Assert.Equal(SubscriptionInvoiceStatus.Paid, result.Status);   // auto-renewed, not a payable invoice
        Assert.Equal(0m, result.Amount);
        var tenant = await db.Tenants.FirstAsync();
        Assert.Equal(TenantStatus.Active, tenant.Status);
        Assert.True(tenant.SubscriptionEndsAt > DateTime.UtcNow.AddDays(20));   // term rolled forward
        Assert.DoesNotContain(
            await db.SubscriptionInvoices.Where(i => i.TenantId == ctx.TenantId).ToListAsync(),
            i => i.Status == SubscriptionInvoiceStatus.Pending);
    }

    [Fact]
    public async Task Renew_NotDueYet_Throws()
    {
        var (db, ctx) = NewDb();
        await SeedAsync(db, ctx.TenantId, DateTime.UtcNow.AddDays(60));   // plenty of paid time left
        var handler = new RenewSubscriptionCommandHandler(db, ctx, new Auth.FakeAppSettings(), new NoOpSubscriptionInvoiceDelivery());

        await Assert.ThrowsAsync<ValidationException>(() =>
            handler.Handle(new RenewSubscriptionCommand(), CancellationToken.None));
    }

    [Fact]
    public async Task GetSubscriptionInvoices_ReturnsOnlyCurrentTenants()
    {
        var (db, ctx) = NewDb();
        await SeedAsync(db, ctx.TenantId, DateTime.UtcNow.AddDays(-2));
        db.SubscriptionInvoices.Add(PendingInvoice(ctx.TenantId, "SEED-1"));
        db.SubscriptionInvoices.Add(PendingInvoice(ctx.TenantId, "SEED-2"));
        db.SubscriptionInvoices.Add(PendingInvoice(Guid.NewGuid(), "OTHER-1")); // different tenant
        await db.SaveChangesAsync();

        var result = await new GetSubscriptionInvoicesQueryHandler(db, ctx)
            .Handle(new GetSubscriptionInvoicesQuery(), CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.StartsWith("SEED-", r.InvoiceNumber));
    }
}
