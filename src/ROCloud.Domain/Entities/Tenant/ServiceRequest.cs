using ROCloud.Domain.Entities.Common;
using ROCloud.Domain.Enums;

namespace ROCloud.Domain.Entities.Tenant;

/// <summary>An AMC / service ticket. DB table: service_requests.</summary>
public class ServiceRequest : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid? AssignedTechId { get; set; }
    public string TicketNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ServiceType ServiceType { get; set; }
    public ServiceRequestStatus Status { get; set; } = ServiceRequestStatus.Open;
    public ServicePriority Priority { get; set; } = ServicePriority.Medium;
    public DateOnly? ScheduledDate { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ResolutionNotes { get; set; }

    // Navigation
    public Customer? Customer { get; set; }
}
