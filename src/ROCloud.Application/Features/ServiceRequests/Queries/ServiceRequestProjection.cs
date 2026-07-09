using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.ServiceRequests.Dtos;
using ROCloud.Domain.Entities.Tenant;

namespace ROCloud.Application.Features.ServiceRequests.Queries;

/// <summary>Shared SQL-translatable projection from ServiceRequest → ServiceRequestListItemDto.</summary>
internal static class ServiceRequestProjection
{
    public static IQueryable<ServiceRequestListItemDto> ToListItem(
        this IQueryable<ServiceRequest> source, IAppDbContext db) =>
        source.Select(s => new ServiceRequestListItemDto(
            s.Id,
            s.TicketNumber,
            s.CustomerId,
            s.Customer != null ? s.Customer.Name : string.Empty,
            s.Customer != null ? s.Customer.Mobile : null,
            s.Title,
            s.ServiceType.ToString(),
            s.Status.ToString(),
            s.Priority.ToString(),
            s.AssignedTechId,
            db.Users.Where(u => u.Id == s.AssignedTechId).Select(u => u.Name).FirstOrDefault(),
            s.ScheduledDate,
            s.CreatedAt));
}
