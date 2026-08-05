using ROCloud.Domain.Entities.Common;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;

namespace ROCloud.Domain.Entities.Platform;

/// <summary>An RO business account (one row per subscriber). DB table: tenants.</summary>
public class Tenant : BaseEntity
{
    public Guid PlanId { get; set; }

    /// <summary>
    /// A downgrade the owner asked for, taking effect at <see cref="SubscriptionEndsAt"/>. A cheaper
    /// plan is never applied immediately — the tenant keeps what they already paid for until the period
    /// ends (and is never refunded). Null when no change is pending. Cleared if they change their mind
    /// or upgrade again. DB: tenants.scheduled_plan_id.
    /// </summary>
    public Guid? ScheduledPlanId { get; set; }

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

    /// <summary>
    /// Whether GST is charged on this tenant's customer invoices (owner-configurable, §24). Defaults
    /// OFF: most small water suppliers are not GST-registered, and a new tenant that invoices before
    /// touching settings must not emit a tax invoice with no GSTIN. The owner opts in once registered —
    /// and cannot enable it without a GstNumber (see UpdateTenantSettingsCommand).
    /// </summary>
    public bool GstEnabled { get; set; }

    /// <summary>GST rate as a fraction (e.g. 0.18 = 18%). Applied only when <see cref="GstEnabled"/>.</summary>
    public decimal GstRate { get; set; } = 0.18m;

    public string? AddressLine { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Pincode { get; set; }

    /// <summary>
    /// The owner's UPI id (VPA, e.g. "shop@okaxis") used to draw a scan-to-pay QR on customer invoices
    /// and statements. DB: tenants.upi_vpa.
    ///
    /// Money paid through it goes STRAIGHT to this id — ROCloud never sees it and cannot reconcile it,
    /// so the owner still records the payment by hand. Nothing here can verify the id is real or that it
    /// belongs to this tenant, which is why the QR is opt-in (<see cref="UpiQrEnabled"/>), the id is
    /// printed as text beside the QR for the customer to check, and the settings screen warns about it.
    /// </summary>
    public string? UpiVpa { get; set; }

    /// <summary>Name the payer sees in their UPI app. Falls back to the business name when unset.</summary>
    public string? UpiPayeeName { get; set; }

    /// <summary>Opt-in for the scan-to-pay QR. Cannot be turned on without a <see cref="UpiVpa"/>.</summary>
    public bool UpiQrEnabled { get; set; }

    /// <summary>
    /// When the current <see cref="UpiVpa"/> was last confirmed to exist against the payments network,
    /// and the account name it came back registered to. Both are CLEARED whenever the id changes, so a
    /// green tick can never be left standing next to an id nobody has checked.
    /// </summary>
    public DateTime? UpiVerifiedAt { get; set; }

    /// <summary>The registered account name returned by the check — the owner confirms it is theirs.</summary>
    public string? UpiVerifiedName { get; set; }

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
