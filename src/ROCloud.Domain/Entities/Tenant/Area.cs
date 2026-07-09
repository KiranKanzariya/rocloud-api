using ROCloud.Domain.Entities.Common;

namespace ROCloud.Domain.Entities.Tenant;

/// <summary>
/// A delivery area / zone within a tenant. DB table: areas.
/// Note: the areas table has no updated_at column — Phase 3 ignores UpdatedAt.
/// </summary>
public class Area : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? City { get; set; }
    public string? Pincode { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public ICollection<Customer> Customers { get; set; } = new List<Customer>();
    public ICollection<Order> Orders { get; set; } = new List<Order>();
}
