using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Customers.Dtos;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Customers.Queries.GetCustomerById;

public sealed record GetCustomerByIdQuery(Guid Id) : IRequest<CustomerDto>;

public class GetCustomerByIdQueryHandler : IRequestHandler<GetCustomerByIdQuery, CustomerDto>
{
    private readonly IAppDbContext _db;

    public GetCustomerByIdQueryHandler(IAppDbContext db) => _db = db;

    public async Task<CustomerDto> Handle(GetCustomerByIdQuery request, CancellationToken ct)
    {
        var c = await _db.Customers.Include(x => x.Area)
            .FirstOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new NotFoundException("Customer", request.Id);

        var subscriptions = await _db.CustomerSubscriptions.Include(s => s.Product)
            .Where(s => s.CustomerId == c.Id)
            .Select(s => new CustomerSubscriptionDto(
                s.Id, s.Product != null ? s.Product.Name : string.Empty,
                s.Quantity, s.Frequency.ToString(), s.RatePerUnit, s.IsActive))
            .ToListAsync(ct);

        var recentOrders = await _db.Orders
            .Where(o => o.CustomerId == c.Id)
            .OrderByDescending(o => o.OrderDate).ThenByDescending(o => o.CreatedAt)
            .Take(5)
            .Select(o => new CustomerOrderSummaryDto(o.Id, o.OrderDate, o.Status.ToString()))
            .ToListAsync(ct);

        var recentPayments = await _db.Payments
            .Where(p => p.CustomerId == c.Id)
            .OrderByDescending(p => p.PaidAt)
            .Take(5)
            .Select(p => new CustomerPaymentSummaryDto(p.Id, p.Amount, p.PaymentMethod.ToString(), p.PaidAt))
            .ToListAsync(ct);

        // Outstanding is a simple customer ledger (guide §9): everything they've been billed minus
        // everything they've paid. This naturally handles lump-sum payments that clear several orders
        // at once, and standalone "advance" payments with no order/invoice link.
        //   Billed = non-cancelled invoices (gross) + delivered orders not yet covered by an invoice.
        //   Paid   = every completed payment for the customer (doorstep, invoice, or advance).
        var balance = await CustomerBalance.ComputeAsync(_db, c.Id, ct);

        return new CustomerDto(
            c.Id, c.CustomerCode, c.Name, c.Mobile, c.AlternateMobile, c.Email,
            c.AddressLine, c.Landmark, c.Latitude, c.Longitude,
            c.AreaId, c.Area?.Name, c.DeliveryMode.ToString(), c.PaymentPreference.ToString(),
            c.PreferredBottleSize?.ToWire(), c.PreferredLanguage, c.Notes, c.IsActive,
            balance, c.DiscountType.ToString(), c.DiscountValue, c.CreatedAt,
            subscriptions, recentOrders, recentPayments);
    }
}
