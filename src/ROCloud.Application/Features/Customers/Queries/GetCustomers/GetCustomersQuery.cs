using System.Linq.Expressions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Models;
using ROCloud.Application.Features.Customers.Dtos;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Customers.Queries.GetCustomers;

public sealed record GetCustomersQuery(CustomerFilterDto Filter) : IRequest<PagedResult<CustomerListItemDto>>;

public class GetCustomersQueryHandler : IRequestHandler<GetCustomersQuery, PagedResult<CustomerListItemDto>>
{
    private readonly IAppDbContext _db;

    public GetCustomersQueryHandler(IAppDbContext db) => _db = db;

    /// <summary>
    /// Net jars still with a customer (Σ Issue − Σ Return − Σ customer-returned Damage), as a
    /// translatable order-by key. Damaged returns leave the customer's hands too, so they reduce the count
    /// — matching GetCustomerJarBalanceQuery.
    /// </summary>
    private Expression<Func<Customer, int>> JarsOutExpr => c =>
        _db.InventoryMovements
            .Where(m => m.CustomerId == c.Id
                && (m.MovementType == InventoryMovementType.Issue
                    || m.MovementType == InventoryMovementType.Return
                    || m.MovementType == InventoryMovementType.Damage))
            .Sum(m => (int?)(m.MovementType == InventoryMovementType.Issue ? m.Quantity : -m.Quantity)) ?? 0;

    public async Task<PagedResult<CustomerListItemDto>> Handle(GetCustomersQuery request, CancellationToken ct)
    {
        var f = request.Filter;
        var page = Math.Max(1, f.Page);
        var pageSize = Math.Clamp(f.PageSize, 1, 100);

        IQueryable<Customer> query = _db.Customers;

        if (f.AreaId is { } areaId)
            query = query.Where(c => c.AreaId == areaId);
        if (f.IsActive is { } isActive)
            query = query.Where(c => c.IsActive == isActive);
        if (CustomerValidationParse.TryDeliveryMode(f.DeliveryMode, out var dm))
            query = query.Where(c => c.DeliveryMode == dm);
        if (CustomerValidationParse.TryPaymentPreference(f.PaymentPreference, out var pp))
            query = query.Where(c => c.PaymentPreference == pp);
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.ToLower();
            query = query.Where(c =>
                c.Name.ToLower().Contains(s) ||
                (c.Mobile != null && c.Mobile.Contains(s)) ||
                (c.CustomerCode != null && c.CustomerCode.ToLower().Contains(s)));
        }

        var descending = string.Equals(f.SortDir, "desc", StringComparison.OrdinalIgnoreCase);
        query = (f.SortBy?.ToLowerInvariant()) switch
        {
            "name" => descending ? query.OrderByDescending(c => c.Name) : query.OrderBy(c => c.Name),
            "mobile" => descending ? query.OrderByDescending(c => c.Mobile) : query.OrderBy(c => c.Mobile),
            "code" => descending ? query.OrderByDescending(c => c.CustomerCode) : query.OrderBy(c => c.CustomerCode),
            "jarsout" => descending ? query.OrderByDescending(JarsOutExpr) : query.OrderBy(JarsOutExpr),
            _ => descending ? query.OrderBy(c => c.CreatedAt) : query.OrderByDescending(c => c.CreatedAt)
        };

        var total = await query.CountAsync(ct);

        var rows = await query
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(c => new
            {
                c.Id,
                c.CustomerCode,
                c.Name,
                c.Mobile,
                AreaName = c.Area != null ? c.Area.Name : null,
                c.PreferredBottleSize,
                c.DeliveryMode,
                c.PaymentPreference,
                c.IsActive,
                c.DiscountType,
                c.DiscountValue,
                // Customer ledger = billed − paid (mirrors CustomerBalance.ComputeAsync; kept inline
                // as set-based subqueries for paging). Floored in C# below.
                //   Billed = non-cancelled invoices (gross) + delivered orders not covered by an invoice.
                //   Paid   = every completed payment (doorstep / invoice / advance).
                OwedInvoices = _db.Invoices
                    .Where(i => i.CustomerId == c.Id && i.Status != InvoiceStatus.Cancelled)
                    .Sum(i => (decimal?)i.TotalAmount) ?? 0m,
                OwedUninvoicedOrders = _db.OrderItems
                    .Where(oi => oi.Order!.CustomerId == c.Id
                        && oi.Order.Status == OrderStatus.Delivered
                        && !_db.Invoices.Any(inv => inv.CustomerId == c.Id
                            && inv.Status != InvoiceStatus.Cancelled
                            && inv.PeriodFrom != null && inv.PeriodTo != null
                            && oi.Order.OrderDate >= inv.PeriodFrom && oi.Order.OrderDate <= inv.PeriodTo))
                    .Sum(oi => (decimal?)(oi.Quantity * oi.UnitRate)) ?? 0m,
                PaidTotal = _db.Payments
                    .Where(p => p.CustomerId == c.Id && p.Status == PaymentStatus.Completed)
                    .Sum(p => (decimal?)p.Amount) ?? 0m,
                // Net jars still with the customer: Σ Issue − Σ Return − Σ customer-returned Damage.
                JarsOut = _db.InventoryMovements
                    .Where(m => m.CustomerId == c.Id
                        && (m.MovementType == InventoryMovementType.Issue
                            || m.MovementType == InventoryMovementType.Return
                            || m.MovementType == InventoryMovementType.Damage))
                    .Sum(m => (int?)(m.MovementType == InventoryMovementType.Issue ? m.Quantity : -m.Quantity)) ?? 0
            })
            .ToListAsync(ct);

        var items = rows.Select(r => new CustomerListItemDto(
            r.Id, r.CustomerCode, r.Name, r.Mobile, r.AreaName,
            r.PreferredBottleSize?.ToWire(), r.DeliveryMode.ToString(), r.PaymentPreference.ToString(),
            // Signed balance: > 0 owed, < 0 advance/credit (mirrors CustomerBalance.ComputeAsync).
            r.OwedInvoices + r.OwedUninvoicedOrders - r.PaidTotal,
            r.IsActive, r.DiscountType.ToString(), r.DiscountValue, Math.Max(0, r.JarsOut))).ToList();

        return new PagedResult<CustomerListItemDto>(items, total, page, pageSize);
    }
}

/// <summary>Tolerant enum-string parsing for optional filter values.</summary>
internal static class CustomerValidationParse
{
    public static bool TryDeliveryMode(string? v, out DeliveryMode mode)
    {
        if (v is not null && Enum.GetNames<DeliveryMode>().Contains(v)) { mode = Enum.Parse<DeliveryMode>(v); return true; }
        mode = default;
        return false;
    }

    public static bool TryPaymentPreference(string? v, out PaymentPreference pref)
    {
        if (v is not null && Enum.GetNames<PaymentPreference>().Contains(v)) { pref = Enum.Parse<PaymentPreference>(v); return true; }
        pref = default;
        return false;
    }
}
