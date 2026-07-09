namespace ROCloud.Domain.Entities.Tenant;

/// <summary>
/// Join row linking a role to a permission. DB table: role_permissions
/// (composite primary key role_id + permission_id, no surrogate id) — so this is a
/// plain join type, not a BaseEntity.
/// </summary>
public class RolePermission
{
    public Guid RoleId { get; set; }
    public Guid PermissionId { get; set; }

    // Navigation
    public Role? Role { get; set; }
    public Permission? Permission { get; set; }
}
