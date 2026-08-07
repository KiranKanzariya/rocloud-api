using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Features.Subscription.Dtos;
using ROCloud.Application.Features.Subscription.Services;
using ROCloud.Domain.Entities.Platform;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;
using ROCloud.Infrastructure.Pdf;

namespace ROCloud.Application.Tests.Subscriptions;

/// <summary>
/// The cancelled subscription-invoice PDF.
///
/// <para>This document was emailed to the owner as a bill. It is re-rendered on every download, so once
/// the invoice is withdrawn the SAME file must come back saying so — otherwise the copy in their inbox
/// and the copy on their billing page disagree about whether they owe money, and the one that says
/// "PAYMENT DUE" is the one that costs them.</para>
/// </summary>
public class SubscriptionInvoicePdfTests
{
    private static SubscriptionInvoicePdfModel Model(bool cancelled, string? reason) => new(
        "SUB-2026-000042",
        new DateOnly(2026, 8, 27),
        new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1),
        "Basic", "Monthly", "Basic plan — 1 month renewal",
        GrossAmount: 1099m, DiscountAmount: 0m, Amount: 1099m,
        Paid: false, TenantName: "Sharma RO Water", TenantGstin: null,
        Cancelled: cancelled, CancellationReason: reason);

    [Fact]
    public void ACancelledInvoiceRenders()
        => AssertValidPdf(new SubscriptionInvoicePdfGenerator().Generate(
            Model(true, "1 free month granted by ROCloud, covering this period. Nothing to pay.")));

    [Fact]
    public void ACancelledInvoiceWithNoRecordedReasonStillRenders()
        // Rows cancelled before the reason column existed carry NULL. The stamp alone must still print
        // rather than the layout falling over on a missing string.
        => AssertValidPdf(new SubscriptionInvoicePdfGenerator().Generate(Model(true, null)));

    [Fact]
    public void APendingInvoiceStillRenders()
        => AssertValidPdf(new SubscriptionInvoicePdfGenerator().Generate(Model(false, null)));

    [Fact]
    public async Task TheBuilderReadsCancellationFromTheRow_NotFromThePaidFlag()
    {
        // The delivery path passes paid:false for a bill. That must not be the only thing deciding the
        // stamp — a re-download after cancellation has to pick the status up from the row itself.
        var ctx = new TenantContext { TenantId = Guid.NewGuid() };
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"subpdf-{Guid.NewGuid()}").Options, ctx);

        var tenant = new Tenant
        {
            Id = ctx.TenantId, Name = "Sharma RO Water", Subdomain = "sharma",
            OwnerName = "O", OwnerEmail = "o@x.com", OwnerMobile = "9", Status = TenantStatus.Active,
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var invoice = new SubscriptionInvoice
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, InvoiceNumber = "SUB-2026-000042",
            PlanType = "Basic", BillingCycle = "Monthly",
            PeriodStart = new DateOnly(2026, 9, 1), PeriodEnd = new DateOnly(2026, 10, 1),
            GrossAmount = 1099m, DiscountAmount = 0m, Amount = 1099m,
            Status = SubscriptionInvoiceStatus.Cancelled,
            CancellationReason = "1 free month granted by ROCloud, covering this period. Nothing to pay.",
            DueDate = new DateOnly(2026, 9, 1),
        };

        var model = SubscriptionInvoicePdfModelBuilder.Build(invoice, tenant, paid: false);

        Assert.True(model.Cancelled);
        Assert.Equal(invoice.CancellationReason, model.CancellationReason);
        AssertValidPdf(new SubscriptionInvoicePdfGenerator().Generate(model));
    }

    private static void AssertValidPdf(byte[] bytes)
    {
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 1000, "PDF looks empty");
        Assert.Equal("%PDF"u8.ToArray(), bytes[..4]);
    }
}
