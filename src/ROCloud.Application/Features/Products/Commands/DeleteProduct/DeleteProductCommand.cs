using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Enums;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Features.Products.Commands.DeleteProduct;

/// <summary>Soft-deletes a product. Blocked while it has open orders or active subscriptions.</summary>
public sealed record DeleteProductCommand(Guid Id) : IRequest;

public class DeleteProductCommandHandler : IRequestHandler<DeleteProductCommand>
{
    private static readonly OrderStatus[] OpenStatuses =
        [OrderStatus.Pending, OrderStatus.Confirmed, OrderStatus.InTransit];

    private readonly IAppDbContext _db;

    public DeleteProductCommandHandler(IAppDbContext db) => _db = db;

    public async Task Handle(DeleteProductCommand request, CancellationToken ct)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == request.Id, ct)
                      ?? throw new NotFoundException("Product", request.Id);

        var hasOpenOrders = await _db.OrderItems
            .AnyAsync(i => i.ProductId == product.Id && i.Order != null && OpenStatuses.Contains(i.Order.Status), ct);
        var hasActiveSubscriptions = await _db.CustomerSubscriptions
            .AnyAsync(s => s.ProductId == product.Id && s.IsActive, ct);

        if (hasOpenOrders || hasActiveSubscriptions)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["product"] = ["This product is referenced by open orders or active subscriptions and cannot be deleted."]
            });

        product.IsDeleted = true;
        await _db.SaveChangesAsync(ct);
    }
}
