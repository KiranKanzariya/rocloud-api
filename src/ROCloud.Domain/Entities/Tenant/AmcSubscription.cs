using ROCloud.Domain.Entities.Common;

namespace ROCloud.Domain.Entities.Tenant;

/// <summary>
/// A customer's Annual Maintenance Contract. DB table: amc_subscriptions. Drives routine
/// AMC visit scheduling: <see cref="NextDueDate"/> is advanced by <see cref="IntervalMonths"/>
/// each time a visit is scheduled.
/// </summary>
public class AmcSubscription : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid CustomerId { get; set; }
    public string? PlanName { get; set; }

    /// <summary>Service cadence in months — 3, 6, or 12.</summary>
    public int IntervalMonths { get; set; }

    /// <summary>Contract fee.</summary>
    public decimal Amount { get; set; }

    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public DateOnly? LastServiceDate { get; set; }

    /// <summary>Date the next routine visit is due. The scheduler reads and advances this.</summary>
    public DateOnly NextDueDate { get; set; }

    public bool IsActive { get; set; } = true;

    // Navigation
    public Customer? Customer { get; set; }
}
