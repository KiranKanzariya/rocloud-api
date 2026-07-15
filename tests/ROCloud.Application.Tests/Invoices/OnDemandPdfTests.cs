using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Features.Invoices.Queries.GetInvoicePdf;
using ROCloud.Application.Features.Subscription.Queries.GetSubscriptionInvoicePdf;
using ROCloud.Domain.Entities.Platform;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Pdf;
using ROCloud.Infrastructure.Persistence;

namespace ROCloud.Application.Tests.Invoices;

/// <summary>
/// Invoice PDFs are never stored — every download re-renders from the invoice row. These cover both
/// kinds (customer invoice, ROCloud subscription invoice) with no file storage in play at all: if a
/// handler ever went back to reading a stored file, it could not satisfy these.
/// </summary>
public class OnDemandPdfTests
{
    private static readonly Guid TenantA = Guid.NewGuid();

    private static AppDbContext NewDb() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"ondemand-pdf-{Guid.NewGuid()}").Options,
        new TenantContext { TenantId = TenantA });

    [Fact]
    public async Task CustomerInvoicePdf_IsRenderedFromTheDatabase()
    {
        using var db = NewDb();
        var customerId = Guid.NewGuid();

        db.Tenants.Add(new Tenant
        {
            Id = TenantA, Name = "Blue Drop Water", Subdomain = "blue",
            OwnerName = "O", OwnerEmail = "o@x.com", OwnerMobile = "9"
        });
        db.Customers.Add(new Customer
        {
            Id = customerId, TenantId = TenantA, Name = "Asha Patel",
            Mobile = "9876543210", CustomerCode = "C-1"
        });
        var invoice = new Invoice
        {
            Id = Guid.NewGuid(), TenantId = TenantA, CustomerId = customerId,
            InvoiceNumber = "INV-202607-0001",
            InvoiceDate = new DateOnly(2026, 7, 1), DueDate = new DateOnly(2026, 7, 8),
            SubTotal = 600m, TotalAmount = 600m, Status = InvoiceStatus.Sent
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var handler = new GetInvoicePdfQueryHandler(db, new InvoicePdfGenerator());
        var result = await handler.Handle(new GetInvoicePdfQuery(invoice.Id), CancellationToken.None);

        Assert.Equal("INV-202607-0001.pdf", result.FileName);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(result.Content, 0, 4));
    }

    [Fact]
    public async Task SubscriptionInvoicePdf_IsRenderedFromTheDatabase()
    {
        using var db = NewDb();
        var tenantCtx = new TenantContext { TenantId = TenantA };

        db.Tenants.Add(new Tenant
        {
            Id = TenantA, Name = "Blue Drop Water", Subdomain = "blue",
            OwnerName = "O", OwnerEmail = "o@x.com", OwnerMobile = "9"
        });
        var invoice = new SubscriptionInvoice
        {
            Id = Guid.NewGuid(), TenantId = TenantA, InvoiceNumber = "SUB-2026-000001",
            PlanType = "Pro", BillingCycle = "Monthly",
            PeriodStart = new DateOnly(2026, 7, 1), PeriodEnd = new DateOnly(2026, 8, 1),
            GrossAmount = 999m, Amount = 999m, Status = "Paid",
            DueDate = new DateOnly(2026, 7, 8)
        };
        db.SubscriptionInvoices.Add(invoice);
        await db.SaveChangesAsync();

        var handler = new GetSubscriptionInvoicePdfQueryHandler(
            db, tenantCtx, new SubscriptionInvoicePdfGenerator());
        var result = await handler.Handle(
            new GetSubscriptionInvoicePdfQuery(invoice.Id), CancellationToken.None);

        Assert.Equal("SUB-2026-000001.pdf", result.FileName);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(result.Bytes, 0, 4));
    }
}
