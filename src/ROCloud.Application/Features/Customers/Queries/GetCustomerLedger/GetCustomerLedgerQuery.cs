using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Customers.Dtos;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Customers.Queries.GetCustomerLedger;

/// <summary>
/// A customer's jar movement for one month, day by day: jars out, empties back, jars still held, and the
/// money. Read-only and derived — nothing is stored.
///
/// <para>
/// Built from <c>inventory_movements</c> rather than from orders, on purpose. That table is the jar
/// ledger: deliveries write to it (through <c>InventoryService</c>, stamped on the day the jars actually
/// moved, so a backdated delivery lands in ITS month), standalone returns write to it, and so does the
/// opening balance. Deriving the running total from the same rows the jar-balance endpoint sums means
/// the two can never drift; reconstructing it from orders would be a second definition of "held".
/// </para>
/// </summary>
public sealed record GetCustomerLedgerQuery(Guid CustomerId, string Month)
    : IRequest<CustomerLedgerDto>;

public class GetCustomerLedgerQueryValidator : AbstractValidator<GetCustomerLedgerQuery>
{
    public GetCustomerLedgerQueryValidator()
    {
        RuleFor(q => q.CustomerId).NotEmpty();
        RuleFor(q => q.Month)
            .Must(m => TryParseMonth(m, out _))
            .WithMessage("Month must be in YYYY-MM format (e.g. 2026-07).");
    }

    /// <summary>Parses "YYYY-MM" to the first of that month. Rejects anything else, including a full date.</summary>
    public static bool TryParseMonth(string? month, out DateOnly firstOfMonth)
    {
        firstOfMonth = default;
        if (string.IsNullOrWhiteSpace(month) || month.Length != 7 || month[4] != '-') return false;
        if (!int.TryParse(month[..4], out var year) || !int.TryParse(month[5..], out var m)) return false;
        if (year is < 2000 or > 2999 || m is < 1 or > 12) return false;

        firstOfMonth = new DateOnly(year, m, 1);
        return true;
    }
}

public class GetCustomerLedgerQueryHandler : IRequestHandler<GetCustomerLedgerQuery, CustomerLedgerDto>
{
    private readonly IAppDbContext _db;

    public GetCustomerLedgerQueryHandler(IAppDbContext db) => _db = db;

    public async Task<CustomerLedgerDto> Handle(GetCustomerLedgerQuery request, CancellationToken ct)
    {
        var exists = await _db.Customers.AsNoTracking()
            .AnyAsync(c => c.Id == request.CustomerId, ct);
        if (!exists) throw new NotFoundException("Customer", request.CustomerId);

        GetCustomerLedgerQueryValidator.TryParseMonth(request.Month, out var monthStart);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var startUtc = AppTimeZone.StartOfDayUtc(monthStart);
        var endUtcExclusive = AppTimeZone.StartOfDayUtc(monthStart.AddMonths(1));

        // The three types that move jars between the plant and a customer. Restock/Adjustment are
        // plant-side stock corrections with no customer, so they never belong on a customer's ledger.
        var jarTypes = new[]
        {
            InventoryMovementType.Issue,
            InventoryMovementType.Return,
            InventoryMovementType.Damage
        };

        // Everything the customer held when the month began, per product. Same sum as the jar-balance
        // endpoint, cut off at the month boundary, so the first row's Rem continues from a real figure.
        var opening = (await _db.InventoryMovements.AsNoTracking()
                .Where(m => m.CustomerId == request.CustomerId
                            && jarTypes.Contains(m.MovementType)
                            && m.CreatedAt < startUtc)
                .Select(m => new { m.ProductId, m.MovementType, m.Quantity })
                .ToListAsync(ct))
            .GroupBy(m => m.ProductId)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(m => m.MovementType == InventoryMovementType.Issue ? m.Quantity : -m.Quantity));

        var movements = await _db.InventoryMovements.AsNoTracking()
            .Where(m => m.CustomerId == request.CustomerId
                        && jarTypes.Contains(m.MovementType)
                        && m.CreatedAt >= startUtc && m.CreatedAt < endUtcExclusive)
            .Select(m => new
            {
                m.Id,
                m.CreatedAt,
                m.MovementType,
                m.Quantity,
                m.ProductId,
                m.OrderId,
                ProductName = m.Product!.Name,
                m.Product.BottleSize
            })
            .ToListAsync(ct);

        // Rates live on the order line, not on the movement. One lookup keyed by (order, product) prices
        // every issued row without a query per row.
        var orderIds = movements.Where(m => m.OrderId is not null).Select(m => m.OrderId!.Value).Distinct().ToList();
        var rates = orderIds.Count == 0
            ? []
            : (await _db.OrderItems.AsNoTracking()
                    .Where(oi => orderIds.Contains(oi.OrderId))
                    .Select(oi => new { oi.OrderId, oi.ProductId, oi.UnitRate })
                    .ToListAsync(ct))
                .GroupBy(oi => (oi.OrderId, oi.ProductId))
                .ToDictionary(g => g.Key, g => g.First().UnitRate);

        // Which days this month are already covered by an invoice. Cancelled invoices bill nothing.
        var periods = await _db.Invoices.AsNoTracking()
            .Where(i => i.CustomerId == request.CustomerId
                        && i.Status != InvoiceStatus.Cancelled
                        && i.PeriodFrom != null && i.PeriodTo != null
                        && i.PeriodFrom <= monthEnd && i.PeriodTo >= monthStart)
            .Select(i => new { From = i.PeriodFrom!.Value, To = i.PeriodTo!.Value })
            .ToListAsync(ct);

        // One ordered pass, OLDEST FIRST — a running total can only be accumulated forwards. The list is
        // reversed at the end, because what the owner wants on screen is the newest movement, not the
        // 1st of the month. Reversing after the fact keeps each row's Rem attached to the row it belongs
        // to: it still reads as "jars still held after this movement".
        var running = new Dictionary<Guid, int>(opening);
        var rows = new List<CustomerLedgerRowDto>(movements.Count);

        foreach (var m in movements.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id))
        {
            var isIssue = m.MovementType == InventoryMovementType.Issue;
            var date = AppTimeZone.Today(m.CreatedAt);

            running[m.ProductId] = running.GetValueOrDefault(m.ProductId) + (isIssue ? m.Quantity : -m.Quantity);

            var amount = 0m;
            if (isIssue && m.OrderId is { } orderId && rates.TryGetValue((orderId, m.ProductId), out var rate))
                amount = m.Quantity * rate;

            rows.Add(new CustomerLedgerRowDto(
                date,
                m.MovementType.ToString() switch
                {
                    nameof(InventoryMovementType.Issue) => "Delivery",
                    nameof(InventoryMovementType.Damage) => "Damage",
                    _ => "Return"
                },
                m.ProductId,
                m.ProductName,
                m.BottleSize.ToWire(),
                isIssue ? m.Quantity : 0,
                isIssue ? 0 : m.Quantity,
                running[m.ProductId],
                amount,
                periods.Any(p => date >= p.From && date <= p.To)));
        }

        // Newest first, the way every other history in the product reads.
        rows.Reverse();

        // Per-product summary, so the client can group without doing its own arithmetic. Ordered by
        // name for a stable strip: ordering by activity would reshuffle the groups as the month fills up.
        var products = rows
            .GroupBy(r => r.ProductId)
            .Select(g =>
            {
                var first = g.First();
                return new CustomerLedgerProductDto(
                    g.Key,
                    first.ProductName,
                    first.BottleSize,
                    opening.GetValueOrDefault(g.Key),
                    running.GetValueOrDefault(g.Key),
                    g.Sum(r => r.Put),
                    g.Sum(r => r.Emp),
                    g.Sum(r => r.Amount));
            })
            .OrderBy(p => p.ProductName)
            .ThenBy(p => p.BottleSize)
            .ToList();

        return new CustomerLedgerDto(
            request.Month,
            opening.Values.Sum(),
            running.Values.Sum(),
            rows.Sum(r => r.Put),
            rows.Sum(r => r.Emp),
            rows.Sum(r => r.Amount),
            rows,
            products);
    }
}
