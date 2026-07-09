using ROCloud.Domain.Entities.Common;

namespace ROCloud.Domain.Entities.Platform;

/// <summary>
/// A cross-tenant support ticket handled by the platform team (guide §26 Support). References a
/// tenant and may be assigned to a platform staff member. Not tenant-scoped (platform-owned).
/// DB table: support_tickets.
/// </summary>
public class SupportTicket : BaseEntity
{
    public Guid TenantId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = "Open";    // Open | InProgress | Resolved | Closed
    public string Priority { get; set; } = "Medium"; // Low | Medium | High | Urgent
    public Guid? AssignedPlatformUserId { get; set; }
    public string? ResolutionNote { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
    public PlatformUser? AssignedPlatformUser { get; set; }
}
