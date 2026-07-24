using ROCloud.Domain.Entities.Common;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;

namespace ROCloud.Domain.Entities.Platform;

/// <summary>An RO business account (one row per subscriber). DB table: tenants.</summary>
public class Tenant : BaseEntity
{
    public Guid PlanId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string OwnerEmail { get; set; } = string.Empty;
    public string OwnerMobile { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string? PrimaryColor { get; set; } = "#0C447C";
    public TenantStatus Status { get; set; } = TenantStatus.Trial;
    public DateTime? TrialEndsAt { get; set; }
    public DateTime? SubscriptionEndsAt { get; set; }
    /// <summary>When the tenant was suspended (null unless currently suspended). Lets a reactivation
    /// credit back the paid days a subscriber lost while blocked. DB: tenants.suspended_at.</summary>
    public DateTime? SuspendedAt { get; set; }
    public string? RazorpaySubscriptionId { get; set; }
    public string? RazorpayCustomerId { get; set; }
    public string? GstNumber { get; set; }

    /// <summary>Whether GST is charged on this tenant's customer invoices (owner-configurable, §24).</summary>
    public bool GstEnabled { get; set; } = true;

    /// <summary>GST rate as a fraction (e.g. 0.18 = 18%). Applied only when <see cref="GstEnabled"/>.</summary>
    public decimal GstRate { get; set; } = 0.18m;

    public string? AddressLine { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Pincode { get; set; }

    /// <summary>Tenant default language (§4c.3). DB: tenants.default_language.</summary>
    public string DefaultLanguage { get; set; } = "en";

    /// <summary>Standing discount on this tenant's ROCloud subscription price (platform-set, guide §26).</summary>
    public SubscriptionDiscountType SubscriptionDiscountType { get; set; } = SubscriptionDiscountType.None;

    /// <summary>Percentage (0–100) or fixed ₹ off the plan price, per <see cref="SubscriptionDiscountType"/>.</summary>
    public decimal SubscriptionDiscountValue { get; set; }

    // Navigation
    public Plan? Plan { get; set; }
    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<Role> Roles { get; set; } = new List<Role>();
    public ICollection<Area> Areas { get; set; } = new List<Area>();
    public ICollection<Product> Products { get; set; } = new List<Product>();
    public ICollection<Customer> Customers { get; set; } = new List<Customer>();
}
