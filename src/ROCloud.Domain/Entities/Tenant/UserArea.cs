using ROCloud.Domain.Entities.Common;

namespace ROCloud.Domain.Entities.Tenant;

/// <summary>
/// Assigns a team member to a delivery area (many-to-many). A delivery boy can serve several
/// areas. DB table: user_areas. The table has no updated_at/is_deleted columns — Phase 3
/// ignores those BaseEntity members; rows are hard-replaced on reassignment.
/// </summary>
public class UserArea : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public Guid AreaId { get; set; }

    // Navigation
    public Area? Area { get; set; }
}
