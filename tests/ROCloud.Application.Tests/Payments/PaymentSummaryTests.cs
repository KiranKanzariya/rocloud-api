using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Features.Payments.Queries.GetPaymentSummary;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;

namespace ROCloud.Application.Tests.Payments;

/// <summary>
/// The money tiles are summed in the DB. They used to be a reduce over one fetched page of payments,
/// and the list endpoint caps a page at 100 — so a busy day silently showed less money than was taken.
/// </summary>
public class PaymentSummaryTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 7, 10);

    private static (AppDbContext Db, TenantContext Ctx) NewDb()
    {
        var ctx = new TenantContext { TenantId = TenantA };
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"pay-summary-{Guid.NewGuid()}").Options, ctx);
        return (db, ctx);
    }

    private static Payment Row(decimal amount, PaymentMethod method, DateOnly day, PaymentStatus status = PaymentStatus.Completed) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = TenantA,
        CustomerId = Guid.NewGuid(),
        Amount = amount,
        PaymentMethod = method,
        Status = status,
        PaidAt = day.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc)
    };

    [Fact]
    public async Task SumsEveryPaymentInTheWindow_NotJustTheFirstPage()
    {
        var (db, _) = NewDb();
        // 250 payments of ₹10 — well past the list endpoint's 100-row page cap.
        for (var i = 0; i < 250; i++) db.Payments.Add(Row(10m, PaymentMethod.Cash, Day));
        await db.SaveChangesAsync();

        var result = await new GetPaymentSummaryQueryHandler(db)
            .Handle(new GetPaymentSummaryQuery(Day, Day), CancellationToken.None);

        Assert.Equal(2500m, result.Collected);   // not 1000 (100 rows) and not 5000
        Assert.Equal(250, result.Count);
    }

    [Fact]
    public async Task SplitsByMethod_AndLumpsTheRestIntoOther()
    {
        var (db, _) = NewDb();
        db.Payments.Add(Row(100m, PaymentMethod.Cash, Day));
        db.Payments.Add(Row(50m, PaymentMethod.Cash, Day));
        db.Payments.Add(Row(200m, PaymentMethod.UPI, Day));
        db.Payments.Add(Row(75m, PaymentMethod.Card, Day));
        await db.SaveChangesAsync();

        var result = await new GetPaymentSummaryQueryHandler(db)
            .Handle(new GetPaymentSummaryQuery(Day, Day), CancellationToken.None);

        Assert.Equal(425m, result.Collected);
        Assert.Equal(150m, result.Cash);
        Assert.Equal(200m, result.Upi);
        Assert.Equal(75m, result.Other);
    }

    [Fact]
    public async Task CountsCompletedOnly_AnAbandonedCheckoutIsNotCollection()
    {
        var (db, _) = NewDb();
        db.Payments.Add(Row(100m, PaymentMethod.Cash, Day));
        db.Payments.Add(Row(999m, PaymentMethod.Online, Day, PaymentStatus.Pending));
        db.Payments.Add(Row(500m, PaymentMethod.Online, Day, PaymentStatus.Failed));
        await db.SaveChangesAsync();

        var result = await new GetPaymentSummaryQueryHandler(db)
            .Handle(new GetPaymentSummaryQuery(Day, Day), CancellationToken.None);

        Assert.Equal(100m, result.Collected);
        Assert.Equal(1, result.Count);
    }

    [Fact]
    public async Task RespectsTheDateWindow()
    {
        var (db, _) = NewDb();
        db.Payments.Add(Row(100m, PaymentMethod.Cash, Day.AddDays(-1)));
        db.Payments.Add(Row(200m, PaymentMethod.Cash, Day));
        db.Payments.Add(Row(400m, PaymentMethod.Cash, Day.AddDays(1)));
        await db.SaveChangesAsync();

        var result = await new GetPaymentSummaryQueryHandler(db)
            .Handle(new GetPaymentSummaryQuery(Day, Day), CancellationToken.None);

        Assert.Equal(200m, result.Collected);
    }
}
