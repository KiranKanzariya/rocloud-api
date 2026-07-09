using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Enums;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Features.Customers.Commands.DeleteCustomer;

/// <summary>Soft-deletes a customer. Blocked while the customer has open (non-terminal) orders.</summary>
public sealed record DeleteCustomerCommand(Guid Id) : IRequest;

public class DeleteCustomerCommandHandler : IRequestHandler<DeleteCustomerCommand>
{
    private static readonly OrderStatus[] OpenStatuses =
        [OrderStatus.Pending, OrderStatus.Confirmed, OrderStatus.InTransit];

    private readonly IAppDbContext _db;

    public DeleteCustomerCommandHandler(IAppDbContext db) => _db = db;

    public async Task Handle(DeleteCustomerCommand request, CancellationToken ct)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == request.Id, ct)
                       ?? throw new NotFoundException("Customer", request.Id);

        var hasOpenOrders = await _db.Orders
            .AnyAsync(o => o.CustomerId == customer.Id && OpenStatuses.Contains(o.Status), ct);
        if (hasOpenOrders)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["customer"] = ["This customer has open orders and cannot be deleted."]
            });

        customer.IsDeleted = true;
        await _db.SaveChangesAsync(ct);
    }
}
