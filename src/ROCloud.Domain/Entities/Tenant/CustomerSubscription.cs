using ROCloud.Domain.Entities.Common;
using ROCloud.Domain.Enums;

namespace ROCloud.Domain.Entities.Tenant;

/// <summary>A recurring delivery subscription for a customer. DB table: customer_subscriptions.</summary>
public class CustomerSubscription : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid ProductId { get; set; }
    public int Quantity { get; set; } = 1;
    public SubscriptionFrequency Frequency { get; set; } = SubscriptionFrequency.Daily;
    public decimal RatePerUnit { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public Customer? Customer { get; set; }
    public Product? Product { get; set; }
}
