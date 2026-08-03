using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Statements.Dtos;
using ROCloud.Domain.Enums;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Features.Statements.Queries.GetCustomerStatementPdf;

/// <summary>
/// Builds a customer's delivery statement for [From, To] — a read-only extract of what was supplied,
/// rendered on every request like the invoice PDF (nothing is stored). Overlapping an invoice period is
/// fine and expected: the statement bills nothing, so no guard applies.
/// </summary>
public sealed record GetCustomerStatementPdfQuery(Guid CustomerId, DateOnly From, DateOnly To)
    : IRequest<StatementPdfResult>;

public sealed record StatementPdfResult(byte[] Content, string FileName);

public class GetCustomerStatementPdfQueryValidator : AbstractValidator<GetCustomerStatementPdfQuery>
{
    /// <summary>A year and a day, so a full-year statement (01 Jan – 31 Dec) is inside the cap.</summary>
    public const int MaxRangeDays = 366;

    public GetCustomerStatementPdfQueryValidator()
    {
        RuleFor(q => q.CustomerId).NotEmpty();
        RuleFor(q => q.To).GreaterThanOrEqualTo(q => q.From)
            .WithMessage("The end date must be on or after the start date.");
        // A statement lists one row per delivered line, so an unbounded range is a runaway PDF.
        RuleFor(q => q)
            .Must(q => q.To.DayNumber - q.From.DayNumber < MaxRangeDays)
            .WithMessage($"A statement can cover at most {MaxRangeDays} days.")
            .When(q => q.To >= q.From);
    }
}

public class GetCustomerStatementPdfQueryHandler
    : IRequestHandler<GetCustomerStatementPdfQuery, StatementPdfResult>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IStatementPdfGenerator _pdf;

    public GetCustomerStatementPdfQueryHandler(
        IAppDbContext db, ITenantContext tenant, IStatementPdfGenerator pdf)
    {
        _db = db;
        _tenant = tenant;
        _pdf = pdf;
    }

    public async Task<StatementPdfResult> Handle(GetCustomerStatementPdfQuery request, CancellationToken ct)
    {
        var customer = await _db.Customers.AsNoTracking()
                           .FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct)
                       ?? throw new NotFoundException("Customer", request.CustomerId);

        var tenant = await _db.Tenants.AsNoTracking()
                         .FirstOrDefaultAsync(t => t.Id == _tenant.TenantId, ct)
                     ?? throw new NotFoundException("Tenant", _tenant.TenantId);

        // One row per delivered order line. Zero-quantity lines are excluded for the same reason the
        // invoice excludes them: a stop closed with nothing handed over supplied nothing.
        var rows = await _db.OrderItems.AsNoTracking()
            .Where(oi => oi.Order!.CustomerId == customer.Id
                         && oi.Order.Status == OrderStatus.Delivered
                         && oi.Order.OrderDate >= request.From
                         && oi.Order.OrderDate <= request.To
                         && oi.Quantity > 0)
            .Select(oi => new
            {
                oi.Id,
                Date = oi.Order!.OrderDate,
                oi.Order.CreatedAt,
                ProductName = oi.Product!.Name,
                oi.Product.BottleSize,
                oi.Quantity,
                oi.UnitRate
            })
            .ToListAsync(ct);

        // Empties taken back on the same delivery, per line. Read separately and joined in memory so the
        // shape stays provider-agnostic (a correlated Sum() is the kind of thing that differs by provider).
        var itemIds = rows.Select(r => r.Id).ToList();
        var returnedByItem = (await _db.DeliveryItems.AsNoTracking()
                .Where(di => itemIds.Contains(di.OrderItemId))
                .Select(di => new { di.OrderItemId, di.JarsReturned })
                .ToListAsync(ct))
            .GroupBy(di => di.OrderItemId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.JarsReturned));

        var lines = rows
            .OrderBy(r => r.Date).ThenBy(r => r.CreatedAt).ThenBy(r => r.ProductName)
            .Select(r => new StatementLine(
                r.Date,
                $"{r.ProductName} ({r.BottleSize.ToWire()})",
                r.Quantity,
                returnedByItem.GetValueOrDefault(r.Id),
                r.UnitRate,
                r.Quantity * r.UnitRate))
            .ToList();

        // Empties returned outside a delivery (the customer page's Record return) carry no order, so they
        // would otherwise be missing — and a customer reconciling jar counts would find the totals short.
        // These are stamped on the day the jars actually moved, so an IST day-range filter is exact.
        var fromUtc = AppTimeZone.StartOfDayUtc(request.From);
        var toUtcExclusive = AppTimeZone.StartOfDayUtc(request.To.AddDays(1));
        var standaloneReturns = (await _db.InventoryMovements.AsNoTracking()
                .Where(m => m.CustomerId == customer.Id
                            && m.OrderId == null
                            && (m.MovementType == InventoryMovementType.Return
                                || m.MovementType == InventoryMovementType.Damage)
                            && m.CreatedAt >= fromUtc && m.CreatedAt < toUtcExclusive)
                .Select(m => new
                {
                    m.CreatedAt,
                    m.MovementType,
                    m.Quantity,
                    ProductName = m.Product!.Name,
                    m.Product.BottleSize
                })
                .ToListAsync(ct))
            .OrderBy(m => m.CreatedAt)
            .Select(m => new StatementReturnLine(
                AppTimeZone.Today(m.CreatedAt),
                $"{m.ProductName} ({m.BottleSize.ToWire()})",
                m.Quantity,
                m.MovementType == InventoryMovementType.Damage))
            .ToList();

        if (lines.Count == 0 && standaloneReturns.Count == 0)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["period"] =
                [
                    $"Nothing was delivered to this customer between {request.From:dd MMM yyyy} " +
                    $"and {request.To:dd MMM yyyy}."
                ]
            });

        var productTotals = lines
            .GroupBy(l => l.Description)
            .Select(g => new StatementProductTotal(g.Key, g.Sum(l => l.Delivered)))
            .OrderBy(t => t.Description)
            .ToList();

        // Which invoice(s) bill these orders. Coverage is by date range today; when orders carry an
        // invoice link this becomes an exact lookup and only this block changes.
        var (invoiceNumbers, uninvoiced) = await ResolveInvoiceCoverageAsync(customer.Id, lines, ct);

        var businessAddress = string.Join(", ",
            new[] { tenant.AddressLine, tenant.City, tenant.State, tenant.Pincode }
                .Where(s => !string.IsNullOrWhiteSpace(s)));

        var customerAddress = string.Join(", ",
            new[] { customer.AddressLine, customer.Landmark }.Where(s => !string.IsNullOrWhiteSpace(s)));

        var model = new StatementPdfModel(
            request.From,
            request.To,
            AppTimeZone.Today(DateTime.UtcNow),
            tenant.Name,
            string.IsNullOrWhiteSpace(businessAddress) ? null : businessAddress,
            tenant.GstNumber,
            customer.Name,
            customer.CustomerCode,
            customer.Mobile,
            string.IsNullOrWhiteSpace(customerAddress) ? null : customerAddress,
            lines,
            standaloneReturns,
            productTotals,
            lines.Sum(l => l.Delivered),
            lines.Sum(l => l.Returned) + standaloneReturns.Sum(r => r.Quantity),
            lines.Sum(l => l.Amount),
            invoiceNumbers,
            uninvoiced,
            tenant.PrimaryColor);

        var code = string.IsNullOrWhiteSpace(customer.CustomerCode)
            ? customer.Id.ToString("N")[..8]
            : customer.CustomerCode;
        var fileName = $"Statement-{code}-{request.From:yyyyMMdd}-{request.To:yyyyMMdd}.pdf";

        return new StatementPdfResult(_pdf.Generate(model), fileName);
    }

    /// <summary>
    /// The invoice numbers covering these deliveries, plus how many of them no invoice covers yet — so
    /// the footer can say "billed on X", "partly billed", or "not yet invoiced" without overclaiming.
    /// Cancelled invoices bill nothing, so they are excluded.
    /// </summary>
    private async Task<(IReadOnlyList<string> Numbers, int Uninvoiced)> ResolveInvoiceCoverageAsync(
        Guid customerId, IReadOnlyList<StatementLine> lines, CancellationToken ct)
    {
        var periods = await _db.Invoices.AsNoTracking()
            .Where(i => i.CustomerId == customerId
                        && i.Status != InvoiceStatus.Cancelled
                        && i.PeriodFrom != null && i.PeriodTo != null)
            .Select(i => new { i.InvoiceNumber, i.PeriodFrom, i.PeriodTo })
            .ToListAsync(ct);

        var numbers = new SortedSet<string>();
        var uninvoiced = 0;

        foreach (var line in lines)
        {
            var covering = periods
                .Where(p => line.Date >= p.PeriodFrom!.Value && line.Date <= p.PeriodTo!.Value)
                .Select(p => p.InvoiceNumber)
                .ToList();

            if (covering.Count == 0) uninvoiced++;
            else foreach (var n in covering) numbers.Add(n);
        }

        return (numbers.ToList(), uninvoiced);
    }
}
