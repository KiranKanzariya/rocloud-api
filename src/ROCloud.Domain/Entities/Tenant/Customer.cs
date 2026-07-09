using ROCloud.Domain.Entities.Common;
using ROCloud.Domain.Enums;

namespace ROCloud.Domain.Entities.Tenant;

/// <summary>An end customer of the tenant's RO business. DB table: customers.</summary>
public class Customer : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid? AreaId { get; set; }
    public string? CustomerCode { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Mobile { get; set; }
    public string? AlternateMobile { get; set; }
    public string? Email { get; set; }
    public string? AddressLine { get; set; }
    public string? Landmark { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public DeliveryMode DeliveryMode { get; set; } = DeliveryMode.HomeDelivery;
    public PaymentPreference PaymentPreference { get; set; } = PaymentPreference.PerBottle;
    public BottleSize? PreferredBottleSize { get; set; } = BottleSize.TwentyL;

    /// <summary>Per-customer language for invoices/WhatsApp (§4c.3). DB: customers.preferred_language.</summary>
    public string? PreferredLanguage { get; set; }

    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Standing invoice discount (platform-managed, guide §26). DB: customers.discount_type / discount_value.</summary>
    public CustomerDiscountType DiscountType { get; set; } = CustomerDiscountType.None;
    public decimal DiscountValue { get; set; }

    // Navigation
    public Area? Area { get; set; }
    public ICollection<CustomerSubscription> Subscriptions { get; set; } = new List<CustomerSubscription>();
    public ICollection<Order> Orders { get; set; } = new List<Order>();
    public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<ServiceRequest> ServiceRequests { get; set; } = new List<ServiceRequest>();
}
