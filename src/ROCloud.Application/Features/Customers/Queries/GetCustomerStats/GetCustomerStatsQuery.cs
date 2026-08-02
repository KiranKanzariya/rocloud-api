using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Customers.Dtos;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Customers.Queries.GetCustomerStats;

public sealed record GetCustomerStatsQuery(Guid Id) : IRequest<CustomerStatsDto>;

public class GetCustomerStatsQueryHandler : IRequestHandler<GetCustomerStatsQuery, CustomerStatsDto>
{
    private readonly IAppDbContext _db;

    public GetCustomerStatsQueryHandler(IAppDbContext db) => _db = db;

    public async Task<CustomerStatsDto> Handle(GetCustomerStatsQuery request, CancellationToken ct)
    {
        // Only the id + creation date are needed — project (no tracking, two columns) rather than
        // materialising the whole tracked Customer entity for a read-only stats query.
        var customer = await _db.Customers.AsNoTracking()
            .Where(c => c.Id == request.Id)
            .Select(c => new { c.Id, c.CreatedAt })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Customer", request.Id);

        var lifetimePayments = await _db.Payments
            .Where(p => p.CustomerId == customer.Id && p.Status == PaymentStatus.Completed)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;

        // Start of the current month as a UTC instant, via the app timezone (IST) — not the UTC month,
        // which would push every IST delivery made before 05:30 on the 1st into the previous month.
        var today = AppTimeZone.Today(DateTime.UtcNow);
        var monthStartUtc = AppTimeZone.StartOfDayUtc(new DateOnly(today.Year, today.Month, 1));

        // Item-wise jars delivered = Σ Issue movements per product (same source as the jar-balance
        // card, so it also counts plant-pickup issues that have no delivery-boy visit). Lifetime and
        // this-month totals come from one grouped scan — the month figure is a conditional sum, so
        // showing both costs no extra round-trip.
        var issuedByProduct = await _db.InventoryMovements
            .Where(m => m.CustomerId == customer.Id && m.MovementType == InventoryMovementType.Issue)
            .GroupBy(m => m.ProductId)
            .Select(g => new
            {
                ProductId = g.Key,
                Quantity = g.Sum(m => m.Quantity),
                MonthQuantity = g.Sum(m => m.CreatedAt >= monthStartUtc ? m.Quantity : 0)
            })
            .ToListAsync(ct);

        var productIds = issuedByProduct.Select(x => x.ProductId).ToList();
        var products = await _db.Products
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.BottleSize })
            .ToListAsync(ct);

        var jarsDeliveredByProduct = issuedByProduct
            .Select(x =>
            {
                var p = products.FirstOrDefault(pp => pp.Id == x.ProductId);
                return new JarsDeliveredByProductDto(
                    p?.Name ?? string.Empty, p?.BottleSize.ToWire() ?? string.Empty, x.Quantity, x.MonthQuantity);
            })
            .OrderByDescending(x => x.Quantity)
            .ThenBy(x => x.ProductName)
            .ToList();

        var jarsDelivered = jarsDeliveredByProduct.Sum(x => x.Quantity);
        var monthJarsDelivered = jarsDeliveredByProduct.Sum(x => x.MonthQuantity);

        var monthsActive = Math.Max(1, (DateTime.UtcNow - customer.CreatedAt).TotalDays / 30.0);
        var averageMonthlySpend = Math.Round(lifetimePayments / (decimal)monthsActive, 2);

        return new CustomerStatsDto(
            jarsDelivered, monthJarsDelivered, lifetimePayments, averageMonthlySpend, jarsDeliveredByProduct);
    }
}
