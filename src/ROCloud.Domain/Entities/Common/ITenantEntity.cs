namespace ROCloud.Domain.Entities.Common;

/// <summary>
/// Marks an entity as tenant-scoped. The AppDbContext applies an automatic
/// global query filter (<c>TenantId == current tenant</c>) to every entity
/// implementing this interface (guide §4).
/// </summary>
public interface ITenantEntity
{
    Guid TenantId { get; set; }
}
