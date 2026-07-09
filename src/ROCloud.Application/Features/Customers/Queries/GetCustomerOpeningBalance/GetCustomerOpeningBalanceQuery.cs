using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Customers.Commands.SetCustomerOpeningBalance;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Customers.Queries.GetCustomerOpeningBalance;

/// <summary>Whether (and what) opening balance was seeded for a customer, for the detail-page card.</summary>
public sealed record CustomerOpeningBalanceDto(
    bool IsSet,
    DateOnly? CutoverDate,
    decimal Dues,                 // > 0 owed, < 0 advance
    IReadOnlyList<OpeningBalanceJarDto> Jars);

public sealed record OpeningBalanceJarDto(string ProductName, string BottleSize, int Quantity);

public sealed record GetCustomerOpeningBalanceQuery(Guid CustomerId) : IRequest<CustomerOpeningBalanceDto>;

public class GetCustomerOpeningBalanceQueryHandler
    : IRequestHandler<GetCustomerOpeningBalanceQuery, CustomerOpeningBalanceDto>
{
    private readonly IAppDbContext _db;

    public GetCustomerOpeningBalanceQueryHandler(IAppDbContext db) => _db = db;

    public async Task<CustomerOpeningBalanceDto> Handle(GetCustomerOpeningBalanceQuery request, CancellationToken ct)
    {
        var marker = SetCustomerOpeningBalanceCommand.Marker;
        var id = request.CustomerId;

        var movements = await _db.InventoryMovements
            .Where(m => m.CustomerId == id && m.Notes != null && m.Notes.StartsWith(marker)
                        && m.MovementType == InventoryMovementType.Issue)
            .GroupBy(m => m.ProductId)
            .Select(g => new { ProductId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToListAsync(ct);

        var invoice = await _db.Invoices
            .Where(i => i.CustomerId == id && i.Notes != null && i.Notes.StartsWith(marker))
            .Select(i => new { i.TotalAmount, i.InvoiceDate })
            .FirstOrDefaultAsync(ct);

        var payment = await _db.Payments
            .Where(p => p.CustomerId == id && p.Notes != null && p.Notes.StartsWith(marker))
            .Select(p => new { p.Amount, p.PaidAt })
            .FirstOrDefaultAsync(ct);

        var isSet = movements.Count > 0 || invoice is not null || payment is not null;
        if (!isSet)
            return new CustomerOpeningBalanceDto(false, null, 0m, []);

        // Dues: positive from the opening invoice, negative from the advance payment.
        var dues = invoice?.TotalAmount ?? (payment is not null ? -payment.Amount : 0m);
        var cutover = invoice?.InvoiceDate
                      ?? (payment is not null ? DateOnly.FromDateTime(payment.PaidAt) : (DateOnly?)null);

        var productIds = movements.Select(m => m.ProductId).ToList();
        var products = await _db.Products
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.BottleSize })
            .ToListAsync(ct);

        var jars = movements
            .Select(m =>
            {
                var p = products.FirstOrDefault(x => x.Id == m.ProductId);
                return new OpeningBalanceJarDto(p?.Name ?? string.Empty, p?.BottleSize.ToWire() ?? string.Empty, m.Quantity);
            })
            .OrderByDescending(j => j.Quantity)
            .ToList();

        return new CustomerOpeningBalanceDto(true, cutover, dues, jars);
    }
}
