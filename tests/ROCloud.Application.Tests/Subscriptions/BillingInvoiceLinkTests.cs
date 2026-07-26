using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Platform.Billing.Queries.GetBillingInvoicePdf;
using ROCloud.Application.Features.Subscription.Dtos;
using ROCloud.Domain.Entities.Platform;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;

namespace ROCloud.Application.Tests.Subscriptions;

/// <summary>
/// The admin billing detail opens the invoice a charge paid for. That relies on the transaction
/// carrying SubscriptionInvoiceId, and on the platform PDF query rendering it across tenants (no tenant
/// context). A transaction with no linked invoice (legacy / ₹0 upgrade) yields a clean 404, not bytes.
/// </summary>
public class BillingInvoiceLinkTests
{
    private sealed class FakeSubPdf : ISubscriptionInvoicePdfGenerator
    {
        public byte[] Generate(SubscriptionInvoicePdfModel model) => [1, 2, 3];
    }

    private static AppDbContext NewDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"bil-{Guid.NewGuid()}").Options,
            new TenantContext());

    private static async Task<(Guid TenantId, SubscriptionInvoice Invoice)> SeedInvoiceAsync(AppDbContext db)
    {
        var tenantId = Guid.NewGuid();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId, Name = "Akash Water", Subdomain = "akash",
            OwnerName = "O", OwnerEmail = "o@x.com", OwnerMobile = "9",
            Status = TenantStatus.Active, DefaultLanguage = "en",
        });
        var invoice = new SubscriptionInvoice
        {
            Id = Guid.NewGuid(), TenantId = tenantId, InvoiceNumber = "SUB-2026-000042",
            PlanType = "Basic", BillingCycle = "Monthly",
            PeriodStart = new DateOnly(2026, 7, 1), PeriodEnd = new DateOnly(2026, 8, 1),
            GrossAmount = 1099m, Amount = 1099m, Status = "Paid", DueDate = new DateOnly(2026, 7, 1),
        };
        db.SubscriptionInvoices.Add(invoice);
        await db.SaveChangesAsync();
        return (tenantId, invoice);
    }

    [Fact]
    public async Task LinkedTransaction_RendersTheInvoicePdf_AcrossTenants()
    {
        await using var db = NewDb();
        var (tenantId, invoice) = await SeedInvoiceAsync(db);
        var txn = new PlatformBillingTransaction
        {
            Id = Guid.NewGuid(), TenantId = tenantId, PlanType = "Basic", Amount = 1099m,
            BillingCycle = "Monthly", Status = "Paid", SubscriptionInvoiceId = invoice.Id,
        };
        db.PlatformBillingTransactions.Add(txn);
        await db.SaveChangesAsync();

        var result = await new GetBillingInvoicePdfQueryHandler(db, new FakeSubPdf())
            .Handle(new GetBillingInvoicePdfQuery(txn.Id), CancellationToken.None);

        Assert.NotEmpty(result.Bytes);
        Assert.Equal("SUB-2026-000042.pdf", result.FileName);   // named after the invoice
    }

    [Fact]
    public async Task UnlinkedTransaction_Returns404_RatherThanBytes()
    {
        await using var db = NewDb();
        var (tenantId, _) = await SeedInvoiceAsync(db);
        var txn = new PlatformBillingTransaction
        {
            Id = Guid.NewGuid(), TenantId = tenantId, PlanType = "Basic", Amount = 0m,
            BillingCycle = "Monthly", Status = "Paid", SubscriptionInvoiceId = null,   // free upgrade / legacy
        };
        db.PlatformBillingTransactions.Add(txn);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new GetBillingInvoicePdfQueryHandler(db, new FakeSubPdf())
                .Handle(new GetBillingInvoicePdfQuery(txn.Id), CancellationToken.None));
    }

    [Fact]
    public async Task UnknownTransaction_Returns404()
    {
        await using var db = NewDb();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new GetBillingInvoicePdfQueryHandler(db, new FakeSubPdf())
                .Handle(new GetBillingInvoicePdfQuery(Guid.NewGuid()), CancellationToken.None));
    }
}
