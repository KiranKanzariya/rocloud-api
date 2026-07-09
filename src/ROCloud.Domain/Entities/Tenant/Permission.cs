namespace ROCloud.Domain.Entities.Tenant;

/// <summary>
/// A platform-wide permission lookup row (e.g. "Customers.Create"). DB table: permissions.
/// Despite living in the Tenant folder, permissions are global (no tenant_id) and the
/// table has only id/module/action/code — so this is a plain lookup type, not a BaseEntity.
/// </summary>
public class Permission
{
    public Guid Id { get; set; }
    public string Module { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;

    // Navigation
    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}
