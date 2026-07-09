using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Enums;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Features.Orders.Commands.CancelOrder;

/// <summary>Cancels a Pending order (and its delivery). Non-pending orders cannot be cancelled.</summary>
public sealed record CancelOrderCommand(Guid Id) : IRequest;

public class CancelOrderCommandHandler : IRequestHandler<CancelOrderCommand>
{
    private readonly IAppDbContext _db;

    public CancelOrderCommandHandler(IAppDbContext db) => _db = db;

    public async Task Handle(CancelOrderCommand request, CancellationToken ct)
    {
        var order = await _db.Orders
            .Include(o => o.Delivery)
            .FirstOrDefaultAsync(o => o.Id == request.Id, ct)
            ?? throw new NotFoundException("Order", request.Id);

        if (order.Status != OrderStatus.Pending)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["status"] = ["Only pending orders can be cancelled."]
            });

        order.Status = OrderStatus.Cancelled;
        if (order.Delivery is { } delivery && delivery.Status == DeliveryStatus.Pending)
            delivery.Status = DeliveryStatus.Skipped;

        await _db.SaveChangesAsync(ct);
    }
}
