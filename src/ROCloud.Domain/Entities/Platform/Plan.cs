using ROCloud.Domain.Entities.Common;
using ROCloud.Domain.Enums;

namespace ROCloud.Domain.Entities.Platform;

/// <summary>Subscription plan offered to tenants. DB table: plans.</summary>
public class Plan : BaseEntity
{
    /// <summary>A limit (MaxCustomers/MaxUsers/MaxDeliveryBoys) set to this means unlimited — no cap is
    /// enforced and the UI shows "Unlimited".</summary>
    public const int Unlimited = 0;

    public string Name { get; set; } = string.Empty;
    public PlanType PlanType { get; set; }
    public decimal MonthlyPrice { get; set; }
    public decimal YearlyPrice { get; set; }
    public int MaxCustomers { get; set; } = 200;
    public int MaxUsers { get; set; } = 3;
    public int MaxDeliveryBoys { get; set; } = 1;
    public bool WhatsappEnabled { get; set; }
    public bool CustomRolesEnabled { get; set; }
    public bool MultiBranchEnabled { get; set; }
    public bool ApiAccessEnabled { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public ICollection<Tenant> Tenants { get; set; } = new List<Tenant>();
}
