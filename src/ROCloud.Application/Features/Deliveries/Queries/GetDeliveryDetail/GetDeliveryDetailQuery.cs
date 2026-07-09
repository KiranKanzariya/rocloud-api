using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Deliveries.Dtos;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Deliveries.Queries.GetDeliveryDetail;

/// <summary>
/// What was actually recorded at a stop: per-item jars delivered/returned, any empties returned for
/// products not on the order, plus the collected amount, proof and notes. Powers the read-only summary
/// shown when opening an already-Delivered delivery.
/// </summary>
public sealed record GetDeliveryDetailQuery(Guid Id) : IRequest<DeliveryDetailDto>;

public class GetDeliveryDetailQueryHandler : IRequestHandler<GetDeliveryDetailQuery, DeliveryDetailDto>
{
    private readonly IAppDbContext _db;

    public GetDeliveryDetailQueryHandler(IAppDbContext db) => _db = db;

    public async Task<DeliveryDetailDto> Handle(GetDeliveryDetailQuery request, CancellationToken ct)
    {
        var delivery = await _db.Deliveries
            .Include(d => d.Order).ThenInclude(o => o!.Customer)
            .Include(d => d.Order).ThenInclude(o => o!.OrderItems)
            .Include(d => d.Items)
            .FirstOrDefaultAsync(d => d.Id == request.Id, ct)
            ?? throw new NotFoundException("Delivery", request.Id);

        // Products referenced by the per-item rows + the order, resolved in one lookup.
        var orderProductIds = delivery.Order?.OrderItems.Select(i => i.ProductId).ToHashSet() ?? new();
        var itemProductIds = delivery.Items.Select(i => i.ProductId).ToHashSet();
        var allProductIds = orderProductIds.Concat(itemProductIds).Distinct().ToList();
        var products = await _db.Products
            .Where(p => allProductIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.BottleSize })
            .ToListAsync(ct);
        string Name(Guid id) => products.FirstOrDefault(p => p.Id == id)?.Name ?? string.Empty;
        string Size(Guid id) => products.FirstOrDefault(p => p.Id == id)?.BottleSize.ToWire() ?? string.Empty;

        var items = delivery.Items
            .Select(i => new DeliveryItemDetailDto(Name(i.ProductId), Size(i.ProductId), i.JarsDelivered, i.JarsReturned))
            .ToList();

        // "Other empties" = Return movements tied to this order for products that weren't on it.
        var otherRaw = await _db.InventoryMovements
            .Where(m => m.OrderId == delivery.OrderId
                        && m.MovementType == InventoryMovementType.Return
                        && !orderProductIds.Contains(m.ProductId))
            .GroupBy(m => m.ProductId)
            .Select(g => new { ProductId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToListAsync(ct);
        var otherReturns = otherRaw
            .Select(o => new DeliveryOtherReturnDto(Name(o.ProductId), Size(o.ProductId), o.Quantity))
            .ToList();

        return new DeliveryDetailDto(
            delivery.Id,
            delivery.OrderId,
            delivery.Order?.Customer?.Name ?? string.Empty,
            delivery.Status.ToString(),
            delivery.DeliveredAt,
            delivery.CollectedAmount,
            delivery.PaymentMethod?.ToString(),
            delivery.ProofImageUrl,
            delivery.Notes,
            delivery.JarsDelivered,
            delivery.JarsReturned,
            items,
            otherReturns);
    }
}
