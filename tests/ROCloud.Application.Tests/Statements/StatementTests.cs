using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Statements.Dtos;
using ROCloud.Application.Features.Statements.Queries.GetCustomerStatementPdf;
using ROCloud.Domain.Entities.Platform;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;
using ROCloud.Infrastructure.Pdf;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Tests.Statements;

/// <summary>
/// The delivery statement is proof of supply, not a bill — these pin the properties that keep it safe to
/// issue freely: it never writes, it lists what was actually handed over, and it only claims a delivery
/// is invoiced when an invoice really covers it.
/// </summary>
public class StatementTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly DateOnly Day1 = new(2026, 7, 5);
    private static readonly DateOnly Day2 = new(2026, 7, 9);

    private static (AppDbContext Db, TenantContext Ctx) NewDb()
    {
        var ctx = new TenantContext { TenantId = TenantA };
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"statements-{Guid.NewGuid()}").Options, ctx);
        return (db, ctx);
    }

    /// <summary>Captures the model the handler built so the assertions can read it (the handler itself
    /// only returns PDF bytes).</summary>
    private sealed class RecordingGenerator : IStatementPdfGenerator
    {
        public StatementPdfModel? Model { get; private set; }

        public byte[] Generate(StatementPdfModel model)
        {
            Model = model;
            return new byte[2048];
        }
    }

    private static async Task<Guid> SeedAsync(AppDbContext db, bool withDeliveryItems = true)
    {
        db.Tenants.Add(new Tenant
        {
            Id = TenantA, Name = "Akash Water Supply", Subdomain = "akash",
            OwnerName = "Owner", OwnerEmail = "owner@akash.test", OwnerMobile = "9999999999",
            Status = TenantStatus.Active, DefaultLanguage = "en"
        });

        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        db.Customers.Add(new Customer
        {
            Id = customerId, TenantId = TenantA, Name = "ABC Enterprises",
            CustomerCode = "CUST-00123", Mobile = "9876543210"
        });
        db.Products.Add(new Product
        {
            Id = productId, TenantId = TenantA, Name = "18L Jar",
            BottleSize = BottleSize.EighteenL, DefaultRate = 25m
        });

        void AddDelivered(DateOnly date, int qty, int returned)
        {
            var orderId = Guid.NewGuid();
            var itemId = Guid.NewGuid();
            db.Orders.Add(new Order
            {
                Id = orderId, TenantId = TenantA, CustomerId = customerId,
                OrderDate = date, Status = OrderStatus.Delivered
            });
            db.OrderItems.Add(new OrderItem
            {
                Id = itemId, TenantId = TenantA, OrderId = orderId,
                ProductId = productId, Quantity = qty, UnitRate = 25m
            });

            if (!withDeliveryItems) return;

            var deliveryId = Guid.NewGuid();
            db.Deliveries.Add(new Delivery
            {
                Id = deliveryId, TenantId = TenantA, OrderId = orderId,
                ScheduledDate = date, Status = DeliveryStatus.Delivered
            });
            db.DeliveryItems.Add(new DeliveryItem
            {
                Id = Guid.NewGuid(), TenantId = TenantA, DeliveryId = deliveryId,
                OrderItemId = itemId, ProductId = productId,
                JarsDelivered = qty, JarsReturned = returned
            });
        }

        AddDelivered(Day1, 10, 8);
        AddDelivered(Day2, 6, 6);

        await db.SaveChangesAsync();
        return customerId;
    }

    private static GetCustomerStatementPdfQueryHandler Handler(
        AppDbContext db, TenantContext ctx, IStatementPdfGenerator pdf)
        => new(db, ctx, pdf);

    [Fact]
    public async Task Statement_ListsEachDeliveredLineWithItsDateAndReturns()
    {
        var (db, ctx) = NewDb();
        var customerId = await SeedAsync(db);
        var pdf = new RecordingGenerator();

        var result = await Handler(db, ctx, pdf).Handle(
            new GetCustomerStatementPdfQuery(customerId, Day1, Day2), CancellationToken.None);

        var m = pdf.Model!;
        Assert.Equal(2, m.Lines.Count);
        Assert.Equal(Day1, m.Lines[0].Date);          // ordered by delivery date
        Assert.Equal(Day2, m.Lines[1].Date);
        Assert.Equal(10, m.Lines[0].Delivered);
        Assert.Equal(8, m.Lines[0].Returned);
        Assert.Equal(250m, m.Lines[0].Amount);        // gross: 10 × ₹25, no discount, no GST
        Assert.Equal(16, m.TotalDelivered);
        Assert.Equal(14, m.TotalReturned);
        Assert.Equal(400m, m.TotalAmount);
        Assert.Equal("ABC Enterprises", m.CustomerName);
        Assert.Contains("CUST-00123", result.FileName);
    }

    [Fact]
    public async Task Statement_ExcludesUndeliveredOrdersAndZeroJarLines()
    {
        var (db, ctx) = NewDb();
        var customerId = await SeedAsync(db);

        // A pending order and a stop closed with nothing handed over: neither supplied anything.
        var pendingId = Guid.NewGuid();
        db.Orders.Add(new Order
        {
            Id = pendingId, TenantId = TenantA, CustomerId = customerId,
            OrderDate = Day1, Status = OrderStatus.Pending
        });
        db.OrderItems.Add(new OrderItem
        {
            Id = Guid.NewGuid(), TenantId = TenantA, OrderId = pendingId,
            ProductId = (await db.Products.FirstAsync()).Id, Quantity = 99, UnitRate = 25m
        });

        var zeroId = Guid.NewGuid();
        db.Orders.Add(new Order
        {
            Id = zeroId, TenantId = TenantA, CustomerId = customerId,
            OrderDate = Day1, Status = OrderStatus.Delivered
        });
        db.OrderItems.Add(new OrderItem
        {
            Id = Guid.NewGuid(), TenantId = TenantA, OrderId = zeroId,
            ProductId = (await db.Products.FirstAsync()).Id, Quantity = 0, UnitRate = 25m
        });
        await db.SaveChangesAsync();

        var pdf = new RecordingGenerator();
        await Handler(db, ctx, pdf).Handle(
            new GetCustomerStatementPdfQuery(customerId, Day1, Day2), CancellationToken.None);

        Assert.Equal(2, pdf.Model!.Lines.Count);
        Assert.Equal(16, pdf.Model.TotalDelivered);
    }

    [Fact]
    public async Task Statement_ForARangeWithNothingDelivered_IsRefused()
    {
        var (db, ctx) = NewDb();
        var customerId = await SeedAsync(db);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            Handler(db, ctx, new RecordingGenerator()).Handle(
                new GetCustomerStatementPdfQuery(customerId, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)),
                CancellationToken.None));

        Assert.True(ex.Errors.ContainsKey("period"));
    }

    [Fact]
    public async Task Statement_NamesTheInvoiceThatBillsTheDeliveries()
    {
        var (db, ctx) = NewDb();
        var customerId = await SeedAsync(db);

        db.Invoices.Add(new Invoice
        {
            Id = Guid.NewGuid(), TenantId = TenantA, CustomerId = customerId,
            InvoiceNumber = "INV-202608-0001", InvoiceDate = new DateOnly(2026, 8, 2),
            DueDate = new DateOnly(2026, 8, 17),
            PeriodFrom = new DateOnly(2026, 7, 1), PeriodTo = new DateOnly(2026, 7, 31),
            SubTotal = 400m, TotalAmount = 400m, Status = InvoiceStatus.Sent
        });
        await db.SaveChangesAsync();

        var pdf = new RecordingGenerator();
        await Handler(db, ctx, pdf).Handle(
            new GetCustomerStatementPdfQuery(customerId, Day1, Day2), CancellationToken.None);

        Assert.Equal(["INV-202608-0001"], pdf.Model!.InvoiceNumbers);
        Assert.Equal(0, pdf.Model.UninvoicedOrderCount);
    }

    [Fact]
    public async Task Statement_CountsDeliveriesNoInvoiceCoversAsUninvoiced()
    {
        var (db, ctx) = NewDb();
        var customerId = await SeedAsync(db);

        // Covers the first delivery only — the second is still unbilled, and the footer must not claim it.
        db.Invoices.Add(new Invoice
        {
            Id = Guid.NewGuid(), TenantId = TenantA, CustomerId = customerId,
            InvoiceNumber = "INV-202607-0009", InvoiceDate = Day1, DueDate = Day1,
            PeriodFrom = Day1, PeriodTo = Day1,
            SubTotal = 250m, TotalAmount = 250m, Status = InvoiceStatus.Sent
        });
        // A cancelled invoice bills nothing, so it must not be named.
        db.Invoices.Add(new Invoice
        {
            Id = Guid.NewGuid(), TenantId = TenantA, CustomerId = customerId,
            InvoiceNumber = "INV-202607-0010", InvoiceDate = Day2, DueDate = Day2,
            PeriodFrom = Day2, PeriodTo = Day2,
            SubTotal = 150m, TotalAmount = 150m, Status = InvoiceStatus.Cancelled
        });
        await db.SaveChangesAsync();

        var pdf = new RecordingGenerator();
        await Handler(db, ctx, pdf).Handle(
            new GetCustomerStatementPdfQuery(customerId, Day1, Day2), CancellationToken.None);

        Assert.Equal(["INV-202607-0009"], pdf.Model!.InvoiceNumbers);
        Assert.Equal(1, pdf.Model.UninvoicedOrderCount);
    }

    [Fact]
    public async Task Statement_WritesNothing()
    {
        var (db, ctx) = NewDb();
        var customerId = await SeedAsync(db);
        var before = await db.Invoices.CountAsync() + await db.Orders.CountAsync()
                     + await db.InventoryMovements.CountAsync();

        await Handler(db, ctx, new RecordingGenerator()).Handle(
            new GetCustomerStatementPdfQuery(customerId, Day1, Day2), CancellationToken.None);

        var after = await db.Invoices.CountAsync() + await db.Orders.CountAsync()
                    + await db.InventoryMovements.CountAsync();
        Assert.Equal(before, after);
    }

    [Theory]
    [InlineData("2026-07-10", "2026-07-05", false)]   // end before start
    [InlineData("2026-01-01", "2027-06-01", false)]   // over the 366-day cap
    [InlineData("2026-01-01", "2026-12-31", true)]    // a full year is allowed
    public void Validator_EnforcesTheRange(string from, string to, bool expectValid)
    {
        var result = new GetCustomerStatementPdfQueryValidator().Validate(
            new GetCustomerStatementPdfQuery(Guid.NewGuid(), DateOnly.Parse(from), DateOnly.Parse(to)));

        Assert.Equal(expectValid, result.IsValid);
    }

    [Fact]
    public void Generator_ProducesAValidPdf()
    {
        var model = new StatementPdfModel(
            Day1, Day2, new DateOnly(2026, 8, 2),
            "Akash Water Supply", "Kothariya, Surendranagar, Gujarat, 363030", "24AAAAA0000A1Z5",
            "ABC Enterprises", "CUST-00123", "9876543210", "Plot 14, MIDC",
            [
                new StatementLine(Day1, "18L Jar (18L)", 10, 8, 25m, 250m),
                new StatementLine(Day2, "18L Jar (18L)", 6, 6, 25m, 150m)
            ],
            [new StatementReturnLine(Day2, "20L Jar (20L)", 2, true)],
            [new StatementProductTotal("18L Jar (18L)", 16)],
            TotalDelivered: 16, TotalReturned: 16, TotalAmount: 400m,
            InvoiceNumbers: ["INV-202608-0001"], UninvoicedOrderCount: 0, BrandColor: "#7A1FA2");

        var bytes = new StatementPdfGenerator().Generate(model);

        Assert.True(bytes.Length > 1000, "PDF looks too small to be real.");
        Assert.Equal("%PDF"u8.ToArray(), bytes[..4]);
    }

    [Fact]
    public void Generator_RendersWhenNothingIsInvoicedYet()
    {
        var model = new StatementPdfModel(
            Day1, Day2, new DateOnly(2026, 8, 2),
            "Akash Water Supply", null, null,
            "ABC Enterprises", null, null, null,
            [new StatementLine(Day1, "18L Jar (18L)", 10, 8, 25m, 250m)],
            [], [new StatementProductTotal("18L Jar (18L)", 10)],
            TotalDelivered: 10, TotalReturned: 8, TotalAmount: 250m,
            InvoiceNumbers: [], UninvoicedOrderCount: 1, BrandColor: null);

        Assert.True(new StatementPdfGenerator().Generate(model).Length > 1000);
    }
}
