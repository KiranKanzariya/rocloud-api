using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Features.Invoices.Commands.GenerateInvoice;
using ROCloud.Application.Tests.Auth;
using ROCloud.Domain.Entities.Platform;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Tests.Invoices;

public class InvoiceTests
{
    private static readonly Guid TenantA = Guid.NewGuid();

    private static (AppDbContext Db, TenantContext Ctx) NewDb()
    {
        var ctx = new TenantContext { TenantId = TenantA };
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"invoices-{Guid.NewGuid()}").Options, ctx);
        return (db, ctx);
    }

    private static async Task<(Guid CustomerId, DateOnly From, DateOnly To)> SeedDeliveredOrdersAsync(AppDbContext db)
    {
        // GST is owner-configurable per tenant; the handler reads it off the tenant row. GST now defaults
        // OFF, so enable it explicitly here (registered tenant with a GSTIN) to exercise the 18% path.
        db.Tenants.Add(new Tenant
        {
            Id = TenantA, Name = "Acme Water", Subdomain = "acme",
            OwnerName = "Owner", OwnerEmail = "owner@acme.test", OwnerMobile = "9999999999",
            Status = TenantStatus.Active, DefaultLanguage = "en",
            GstEnabled = true, GstRate = 0.18m, GstNumber = "24AAAAA0000A1Z5"
        });

        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        db.Customers.Add(new Customer
        {
            Id = customerId, TenantId = TenantA, Name = "Ravi", Mobile = "9",
            PaymentPreference = PaymentPreference.Monthly
        });
        db.Products.Add(new Product
        {
            Id = productId, TenantId = TenantA, Name = "20L Jar", BottleSize = BottleSize.TwentyL, DefaultRate = 40m
        });

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        void AddDeliveredOrder(int qty)
        {
            var order = new Order
            {
                Id = Guid.NewGuid(), TenantId = TenantA, CustomerId = customerId,
                OrderDate = today, Status = OrderStatus.Delivered
            };
            order.OrderItems.Add(new OrderItem
            {
                Id = Guid.NewGuid(), TenantId = TenantA, OrderId = order.Id,
                ProductId = productId, Quantity = qty, UnitRate = 40m
            });
            db.Orders.Add(order);
        }

        AddDeliveredOrder(10);  // 400
        AddDeliveredOrder(5);   // 200

        // A non-delivered order that must be excluded from the invoice.
        var pending = new Order
        {
            Id = Guid.NewGuid(), TenantId = TenantA, CustomerId = customerId,
            OrderDate = today, Status = OrderStatus.Pending
        };
        pending.OrderItems.Add(new OrderItem
        {
            Id = Guid.NewGuid(), TenantId = TenantA, OrderId = pending.Id,
            ProductId = productId, Quantity = 99, UnitRate = 40m
        });
        db.Orders.Add(pending);

        await db.SaveChangesAsync();
        var first = new DateOnly(today.Year, today.Month, 1);
        return (customerId, first, first.AddMonths(1).AddDays(-1));
    }

    [Fact]
    public async Task GenerateInvoice_ForCustomerWithDeliveries_CreatesCorrectAmount()
    {
        var (db, ctx) = NewDb();
        var (customerId, from, to) = await SeedDeliveredOrdersAsync(db);

        var handler = new GenerateInvoiceCommandHandler(db, ctx, new FakeAppSettings());
        var id = await handler.Handle(
            new GenerateInvoiceCommand(customerId, from, to, null, null, null, null), CancellationToken.None);

        var invoice = await db.Invoices.FirstAsync(i => i.Id == id);
        Assert.Equal(600m, invoice.SubTotal);          // 400 + 200 (pending order excluded)
        Assert.Equal(108m, invoice.TaxAmount);         // 18% of 600
        Assert.Equal(708m, invoice.TotalAmount);
        Assert.Equal(InvoiceStatus.Draft, invoice.Status);
        Assert.StartsWith($"INV-{from:yyyyMM}-", invoice.InvoiceNumber);
        Assert.EndsWith("0001", invoice.InvoiceNumber);
    }

    [Fact]
    public async Task GenerateInvoice_CustomerPercentage100_ProducesZeroTotal()
    {
        var (db, ctx) = NewDb();
        var (customerId, from, to) = await SeedDeliveredOrdersAsync(db);

        var customer = await db.Customers.FirstAsync(c => c.Id == customerId);
        customer.DiscountType = CustomerDiscountType.Percentage;
        customer.DiscountValue = 100m;
        await db.SaveChangesAsync();

        var handler = new GenerateInvoiceCommandHandler(db, ctx, new FakeAppSettings());
        var id = await handler.Handle(
            new GenerateInvoiceCommand(customerId, from, to, null, null, null, null), CancellationToken.None);

        var invoice = await db.Invoices.FirstAsync(i => i.Id == id);
        Assert.Equal(600m, invoice.SubTotal);
        Assert.Equal(600m, invoice.Discount);   // full subtotal waived
        Assert.Equal(0m, invoice.TaxAmount);     // GST on the discounted (zero) amount
        Assert.Equal(0m, invoice.TotalAmount);
        // Nothing is owed, so it is settled on arrival — not left at Draft, which would contradict the
        // PAID stamp the PDF derives from the balance and would keep the monthly job mailing it.
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
    }

    [Fact]
    public async Task GenerateInvoice_ForAPeriodAlreadyInvoiced_IsRefusedAndNamesTheInvoice()
    {
        var (db, ctx) = NewDb();
        var (customerId, from, to) = await SeedDeliveredOrdersAsync(db);

        var handler = new GenerateInvoiceCommandHandler(db, ctx, new FakeAppSettings());
        var command = new GenerateInvoiceCommand(customerId, from, to, null, null, null, null);
        await handler.Handle(command, CancellationToken.None);

        // A second run for the same period would be summed gross into the customer's balance, doubling
        // what they are chased for — so it is refused, naming the invoice that already covers it.
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => handler.Handle(command, CancellationToken.None));

        Assert.Contains("0001", ex.Errors["period"][0]);
        Assert.Single(await db.Invoices.ToListAsync());
    }

    [Fact]
    public async Task GenerateInvoice_ForAPeriodWhoseOnlyInvoiceIsCancelled_IsAllowed()
    {
        var (db, ctx) = NewDb();
        var (customerId, from, to) = await SeedDeliveredOrdersAsync(db);

        var handler = new GenerateInvoiceCommandHandler(db, ctx, new FakeAppSettings());
        var command = new GenerateInvoiceCommand(customerId, from, to, null, null, null, null);
        var firstId = await handler.Handle(command, CancellationToken.None);

        // Cancelled invoices are excluded from every balance sum, so the period is billable again —
        // this is how an owner re-issues a mistaken invoice.
        (await db.Invoices.FirstAsync(i => i.Id == firstId)).Status = InvoiceStatus.Cancelled;
        await db.SaveChangesAsync();

        var secondId = await handler.Handle(command, CancellationToken.None);

        Assert.NotEqual(firstId, secondId);
        Assert.Equal(2, await db.Invoices.CountAsync());
    }

    [Fact]
    public async Task GenerateInvoice_WhenEveryStopDeliveredZeroJars_IsRefused()
    {
        var (db, ctx) = NewDb();
        var (customerId, from, to) = await SeedDeliveredOrdersAsync(db);

        // The month's stops were all closed with nothing handed over (quantity = jars delivered), so
        // there is nothing to bill — no invoice at all, rather than a ₹0 one with 0-jar lines.
        foreach (var item in await db.OrderItems.ToListAsync())
            item.Quantity = 0;
        await db.SaveChangesAsync();

        var handler = new GenerateInvoiceCommandHandler(db, ctx, new FakeAppSettings());
        var ex = await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(
            new GenerateInvoiceCommand(customerId, from, to, null, null, null, null), CancellationToken.None));

        Assert.True(ex.Errors.ContainsKey("period"));
        Assert.Empty(await db.Invoices.ToListAsync());
    }

    [Fact]
    public async Task GenerateInvoice_CustomerPercentage50_HalvesTheBill()
    {
        var (db, ctx) = NewDb();
        var (customerId, from, to) = await SeedDeliveredOrdersAsync(db);

        var customer = await db.Customers.FirstAsync(c => c.Id == customerId);
        customer.DiscountType = CustomerDiscountType.Percentage;
        customer.DiscountValue = 50m;
        await db.SaveChangesAsync();

        var handler = new GenerateInvoiceCommandHandler(db, ctx, new FakeAppSettings());
        var id = await handler.Handle(
            new GenerateInvoiceCommand(customerId, from, to, null, null, null, null), CancellationToken.None);

        var invoice = await db.Invoices.FirstAsync(i => i.Id == id);
        Assert.Equal(300m, invoice.Discount);    // 50% of 600
        Assert.Equal(54m, invoice.TaxAmount);    // 18% of 300
        Assert.Equal(354m, invoice.TotalAmount);
    }

    [Fact]
    public async Task GenerateInvoice_CustomerFixedDiscount_DeductsAmount()
    {
        var (db, ctx) = NewDb();
        var (customerId, from, to) = await SeedDeliveredOrdersAsync(db);

        var customer = await db.Customers.FirstAsync(c => c.Id == customerId);
        customer.DiscountType = CustomerDiscountType.Fixed;
        customer.DiscountValue = 100m;
        await db.SaveChangesAsync();

        var handler = new GenerateInvoiceCommandHandler(db, ctx, new FakeAppSettings());
        var id = await handler.Handle(
            new GenerateInvoiceCommand(customerId, from, to, null, null, null, null), CancellationToken.None);

        var invoice = await db.Invoices.FirstAsync(i => i.Id == id);
        Assert.Equal(100m, invoice.Discount);
        Assert.Equal(90m, invoice.TaxAmount);    // 18% of 500
        Assert.Equal(590m, invoice.TotalAmount);
    }

    [Fact]
    public async Task GenerateInvoice_ExplicitDiscount_OverridesStandingDiscount()
    {
        var (db, ctx) = NewDb();
        var (customerId, from, to) = await SeedDeliveredOrdersAsync(db);

        var customer = await db.Customers.FirstAsync(c => c.Id == customerId);
        customer.DiscountType = CustomerDiscountType.Percentage;
        customer.DiscountValue = 100m;   // standing "always free"
        await db.SaveChangesAsync();

        var handler = new GenerateInvoiceCommandHandler(db, ctx, new FakeAppSettings());
        // Explicit discount of 0 on this one invoice overrides the standing 100%.
        var id = await handler.Handle(
            new GenerateInvoiceCommand(customerId, from, to, null, 0m, null, null), CancellationToken.None);

        var invoice = await db.Invoices.FirstAsync(i => i.Id == id);
        Assert.Equal(0m, invoice.Discount);
        Assert.Equal(708m, invoice.TotalAmount); // full 600 + 108 GST
    }

    [Fact]
    public async Task GenerateInvoice_NoDeliveries_Throws()
    {
        var (db, ctx) = NewDb();
        var customerId = Guid.NewGuid();
        db.Customers.Add(new Customer { Id = customerId, TenantId = TenantA, Name = "Empty", Mobile = "8" });
        await db.SaveChangesAsync();

        var handler = new GenerateInvoiceCommandHandler(db, ctx, new FakeAppSettings());
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(
            new GenerateInvoiceCommand(customerId, today.AddDays(-30), today, null, null, null, null),
            CancellationToken.None));
    }
}
