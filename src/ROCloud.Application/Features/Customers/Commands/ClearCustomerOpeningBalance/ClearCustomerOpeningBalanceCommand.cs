using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Customers.Commands.SetCustomerOpeningBalance;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Customers.Commands.ClearCustomerOpeningBalance;

/// <summary>
/// Reverses an opening balance seeded by <see cref="SetCustomerOpeningBalanceCommand"/> — for fixing a
/// migration mistake before go-live. Removes only the records tagged with the opening-balance marker:
/// the customer-scoped <c>Issue</c> movements (and rolls their issued stock back), the opening invoice,
/// and the advance payment. A no-op if no opening balance was set.
/// </summary>
public sealed record ClearCustomerOpeningBalanceCommand(Guid CustomerId) : IRequest;

public class ClearCustomerOpeningBalanceCommandHandler : IRequestHandler<ClearCustomerOpeningBalanceCommand>
{
    private readonly IAppDbContext _db;

    public ClearCustomerOpeningBalanceCommandHandler(IAppDbContext db) => _db = db;

    public async Task Handle(ClearCustomerOpeningBalanceCommand request, CancellationToken ct)
    {
        // Tenant query filter + explicit id → cross-tenant access yields NotFound (404).
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct)
                       ?? throw new NotFoundException("Customer", request.CustomerId);

        var marker = SetCustomerOpeningBalanceCommand.Marker;

        // 1) Roll back the opening jar Issues, then remove those movements.
        var movements = await _db.InventoryMovements
            .Where(m => m.CustomerId == customer.Id && m.Notes != null && m.Notes.StartsWith(marker))
            .ToListAsync(ct);

        if (movements.Count > 0)
        {
            var byProduct = movements
                .GroupBy(m => m.ProductId)
                .ToDictionary(g => g.Key, g => g.Where(m => m.MovementType == InventoryMovementType.Issue).Sum(m => m.Quantity));

            var productIds = byProduct.Keys.ToList();
            var inventories = await _db.Inventories.Where(i => productIds.Contains(i.ProductId)).ToListAsync(ct);
            foreach (var inv in inventories)
            {
                inv.IssuedStock -= byProduct[inv.ProductId]; // undo the opening Issue
                inv.LastUpdated = DateTime.UtcNow;
            }

            _db.InventoryMovements.RemoveRange(movements);
        }

        // 2) Remove the advance payment(s) before the invoice (payments.invoice_id FK).
        var payments = await _db.Payments
            .Where(p => p.CustomerId == customer.Id && p.Notes != null && p.Notes.StartsWith(marker))
            .ToListAsync(ct);
        if (payments.Count > 0) _db.Payments.RemoveRange(payments);

        // 3) Remove the opening invoice(s).
        var invoices = await _db.Invoices
            .Where(i => i.CustomerId == customer.Id && i.Notes != null && i.Notes.StartsWith(marker))
            .ToListAsync(ct);
        if (invoices.Count > 0) _db.Invoices.RemoveRange(invoices);

        await _db.SaveChangesAsync(ct);

        // Taking the opening advance away un-pays whatever it had settled — the remaining invoices must
        // stop claiming to be paid. (SyncAsync is a full recompute, so it demotes as well as promotes.)
        await Payments.InvoiceAllocationSync.SyncAsync(_db, customer.Id, ct);
    }
}
