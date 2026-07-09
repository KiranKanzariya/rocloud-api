using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Deliveries.Dtos;
using ROCloud.Domain.Entities.Tenant;

namespace ROCloud.Application.Features.Deliveries.Queries;

/// <summary>Shared SQL-translatable projection from Delivery → DeliveryListItemDto.</summary>
internal static class DeliveryProjection
{
    public static IQueryable<DeliveryListItemDto> ToListItem(this IQueryable<Delivery> source, IAppDbContext db) =>
        source.Select(d => new DeliveryListItemDto(
            d.Id,
            d.OrderId,
            d.Status.ToString(),
            d.Order != null ? d.Order.DeliveryMode.ToString() : null,
            d.ScheduledDate,
            d.DeliveredAt,
            d.Order != null ? d.Order.CustomerId : Guid.Empty,
            d.Order != null && d.Order.Customer != null ? d.Order.Customer.Name : string.Empty,
            d.Order != null && d.Order.Customer != null ? d.Order.Customer.Mobile : null,
            d.Order != null && d.Order.Customer != null ? d.Order.Customer.AddressLine : null,
            d.Order != null && d.Order.Area != null ? d.Order.Area.Name : null,
            d.DeliveryBoyId,
            db.Users.Where(u => u.Id == d.DeliveryBoyId).Select(u => u.Name).FirstOrDefault(),
            d.JarsDelivered,
            d.JarsReturned,
            d.CollectedAmount,
            d.PaymentMethod != null ? d.PaymentMethod.ToString() : null,
            d.ProofImageUrl,
            d.Latitude,
            d.Longitude,
            d.Notes,
            d.Order != null
                ? d.Order.OrderItems
                    .Select(oi => new DeliveryLineDto(oi.Product != null ? oi.Product.Name : string.Empty, oi.Quantity))
                    .ToList()
                : new List<DeliveryLineDto>()));
}
