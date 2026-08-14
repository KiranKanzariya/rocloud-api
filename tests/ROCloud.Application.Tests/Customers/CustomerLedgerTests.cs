using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Features.Customers.Queries.GetCustomerJarBalance;
using ROCloud.Application.Features.Customers.Queries.GetCustomerLedger;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;

namespace ROCloud.Application.Tests.Customers;

public class CustomerLedgerTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();
    private static readonly Guid JarId = Guid.NewGuid();
    private static readonly Guid BottleId = Guid.NewGuid();

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"ledger-{Guid.NewGuid()}").Options,
            new TenantContext { TenantId = TenantA });

    private static async Task SeedAsync(AppDbContext db)
    {
        db.Customers.Add(new Customer { Id = CustomerId, TenantId = TenantA, Name = "Prathvik", Mobile = "9723816724" });
        db.Products.Add(new Product { Id = JarId, TenantId = TenantA, Name = "Water Jar", BottleSize = BottleSize.EighteenL });
        db.Products.Add(new Product { Id = BottleId, TenantId = TenantA, Name = "Water Bottle", BottleSize = BottleSize.TwentyL });
        await db.SaveChangesAsync();
    }

    private static void Move(AppDbContext db, Guid productId, InventoryMovementType type, int qty, DateOnly on,
        Guid? orderId = null)
        => db.InventoryMovements.Add(new InventoryMovement
        {
            Id = Guid.NewGuid(),
            TenantId = TenantA,
            CustomerId = CustomerId,
            ProductId = productId,
            OrderId = orderId,
            MovementType = type,
            Quantity = qty,
            CreatedAt = AppTimeZone.MiddayUtc(on)
        });

    [Fact]
    public async Task Rem_runs_per_product_and_carries_the_opening_balance()
    {
        var db = NewDb();
        await SeedAsync(db);

        // Held before July starts: 2 jars.
        Move(db, JarId, InventoryMovementType.Issue, 2, new DateOnly(2026, 6, 28));
        // July: out 1, back 1, out 1 → jars end at 3. Bottles move independently.
        Move(db, JarId, InventoryMovementType.Issue, 1, new DateOnly(2026, 7, 1));
        Move(db, JarId, InventoryMovementType.Return, 1, new DateOnly(2026, 7, 2));
        Move(db, JarId, InventoryMovementType.Issue, 1, new DateOnly(2026, 7, 3));
        Move(db, BottleId, InventoryMovementType.Issue, 1, new DateOnly(2026, 7, 2));
        await db.SaveChangesAsync();

        var result = await new GetCustomerLedgerQueryHandler(db)
            .Handle(new GetCustomerLedgerQuery(CustomerId, "2026-07"), default);

        Assert.Equal(2, result.OpeningJarsOut);
        Assert.Equal(4, result.ClosingJarsOut);           // 3 jars + 1 bottle
        Assert.Equal(4, result.Rows.Count);               // June's movement is outside the month

        // Newest first — what happened last is what the owner is looking for.
        Assert.Equal(
            result.Rows.Select(r => r.Date).OrderByDescending(d => d).ToList(),
            result.Rows.Select(r => r.Date).ToList());
        Assert.Equal(new DateOnly(2026, 7, 3), result.Rows[0].Date);

        // Rem is per product: the bottle row must not disturb the jar's running count. Read newest
        // first, the jar went 3 (after the 3rd) ← 2 (after the return) ← 3 (after the 1st).
        var jarRems = result.Rows.Where(r => r.ProductId == JarId).Select(r => r.Rem).ToList();
        Assert.Equal([3, 2, 3], jarRems);
        Assert.Equal(1, result.Rows.Single(r => r.ProductId == BottleId).Rem);

        Assert.Equal(3, result.TotalPut);
        Assert.Equal(1, result.TotalEmp);
    }

    [Fact]
    public async Task Groups_by_product_with_its_own_totals()
    {
        // The ledger is read product by product — "how many jars did they take, how many came back" —
        // so each product carries its own opening, closing and money rather than one blended figure.
        var db = NewDb();
        await SeedAsync(db);

        Move(db, JarId, InventoryMovementType.Issue, 2, new DateOnly(2026, 6, 28));   // held before July
        Move(db, JarId, InventoryMovementType.Issue, 3, new DateOnly(2026, 7, 1));
        Move(db, JarId, InventoryMovementType.Return, 1, new DateOnly(2026, 7, 2));
        Move(db, BottleId, InventoryMovementType.Issue, 4, new DateOnly(2026, 7, 3));
        await db.SaveChangesAsync();

        var result = await new GetCustomerLedgerQueryHandler(db)
            .Handle(new GetCustomerLedgerQuery(CustomerId, "2026-07"), default);

        Assert.Equal(2, result.Products.Count);

        var jar = result.Products.Single(p => p.ProductId == JarId);
        Assert.Equal(2, jar.Opening);
        Assert.Equal(4, jar.Closing);      // 2 held + 3 out − 1 back
        Assert.Equal(3, jar.Put);
        Assert.Equal(1, jar.Emp);

        var bottle = result.Products.Single(p => p.ProductId == BottleId);
        Assert.Equal(0, bottle.Opening);   // nothing held before the month
        Assert.Equal(4, bottle.Closing);
        Assert.Equal(0, bottle.Emp);

        // Per-product totals must add up to the month totals, or the group headings and the strip
        // above them would tell the owner two different stories.
        Assert.Equal(result.TotalPut, result.Products.Sum(p => p.Put));
        Assert.Equal(result.TotalEmp, result.Products.Sum(p => p.Emp));
        Assert.Equal(result.TotalAmount, result.Products.Sum(p => p.Amount));
        Assert.Equal(result.ClosingJarsOut, result.Products.Sum(p => p.Closing));
    }

    [Fact]
    public async Task Closing_matches_the_jar_balance_endpoint()
    {
        // The whole reason the ledger is built from inventory_movements: these two numbers are the same
        // fact, and a customer who sees them disagree stops trusting both.
        var db = NewDb();
        await SeedAsync(db);

        var thisMonth = AppTimeZone.Today(DateTime.UtcNow);
        Move(db, JarId, InventoryMovementType.Issue, 5, thisMonth);
        Move(db, JarId, InventoryMovementType.Return, 2, thisMonth);
        Move(db, JarId, InventoryMovementType.Damage, 1, thisMonth);   // broken jars left their hands too
        await db.SaveChangesAsync();

        var ledger = await new GetCustomerLedgerQueryHandler(db)
            .Handle(new GetCustomerLedgerQuery(CustomerId, $"{thisMonth:yyyy-MM}"), default);
        var balance = await new GetCustomerJarBalanceQueryHandler(db)
            .Handle(new GetCustomerJarBalanceQuery(CustomerId), default);

        Assert.Equal(2, ledger.ClosingJarsOut);
        Assert.Equal(ledger.ClosingJarsOut, balance.Sum(b => b.Outstanding));
    }

    [Fact]
    public async Task Prices_issued_rows_from_the_order_line_and_leaves_empties_at_zero()
    {
        var db = NewDb();
        await SeedAsync(db);

        var orderId = Guid.NewGuid();
        var day = new DateOnly(2026, 7, 4);
        db.Orders.Add(new Order
        {
            Id = orderId, TenantId = TenantA, CustomerId = CustomerId,
            OrderDate = day, Status = OrderStatus.Delivered
        });
        db.OrderItems.Add(new OrderItem
        {
            Id = Guid.NewGuid(), TenantId = TenantA, OrderId = orderId,
            ProductId = JarId, Quantity = 2, UnitRate = 35m
        });
        Move(db, JarId, InventoryMovementType.Issue, 2, day, orderId);
        Move(db, JarId, InventoryMovementType.Return, 1, day, orderId);
        await db.SaveChangesAsync();

        var result = await new GetCustomerLedgerQueryHandler(db)
            .Handle(new GetCustomerLedgerQuery(CustomerId, "2026-07"), default);

        var delivery = result.Rows.Single(r => r.Kind == "Delivery");
        var ret = result.Rows.Single(r => r.Kind == "Return");

        Assert.Equal(70m, delivery.Amount);       // 2 × ₹35
        Assert.Equal(0m, ret.Amount);             // returning a jar is not a sale
        Assert.Equal(70m, result.TotalAmount);
    }

    [Fact]
    public async Task Flags_rows_an_invoice_period_covers()
    {
        var db = NewDb();
        await SeedAsync(db);

        db.Invoices.Add(new Invoice
        {
            Id = Guid.NewGuid(), TenantId = TenantA, CustomerId = CustomerId,
            InvoiceNumber = "INV-202607-0001",
            InvoiceDate = new DateOnly(2026, 7, 20), DueDate = new DateOnly(2026, 8, 4),
            PeriodFrom = new DateOnly(2026, 7, 1), PeriodTo = new DateOnly(2026, 7, 15),
            Status = InvoiceStatus.Sent
        });
        Move(db, JarId, InventoryMovementType.Issue, 1, new DateOnly(2026, 7, 10));   // inside the period
        Move(db, JarId, InventoryMovementType.Issue, 1, new DateOnly(2026, 7, 25));   // after it
        await db.SaveChangesAsync();

        var result = await new GetCustomerLedgerQueryHandler(db)
            .Handle(new GetCustomerLedgerQuery(CustomerId, "2026-07"), default);

        Assert.True(result.Rows.Single(r => r.Date.Day == 10).Invoiced);
        Assert.False(result.Rows.Single(r => r.Date.Day == 25).Invoiced);
    }

    [Fact]
    public async Task Unknown_customer_is_a_404_not_an_empty_month()
    {
        var db = NewDb();
        await SeedAsync(db);

        await Assert.ThrowsAsync<NotFoundException>(() => new GetCustomerLedgerQueryHandler(db)
            .Handle(new GetCustomerLedgerQuery(Guid.NewGuid(), "2026-07"), default));
    }

    [Theory]
    [InlineData("2026-07", true)]
    [InlineData("2026-7", false)]
    [InlineData("2026-13", false)]
    [InlineData("2026-07-01", false)]
    [InlineData("", false)]
    public void Month_parsing_accepts_only_YYYY_MM(string month, bool valid)
        => Assert.Equal(valid, GetCustomerLedgerQueryValidator.TryParseMonth(month, out _));
}
