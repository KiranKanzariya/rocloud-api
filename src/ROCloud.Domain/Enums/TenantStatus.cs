namespace ROCloud.Domain.Enums;

/// <summary>Lifecycle state of a tenant. DB: tenants.status.</summary>
public enum TenantStatus
{
    Trial,
    Active,
    Suspended,
    Overdue,
    Cancelled
}
