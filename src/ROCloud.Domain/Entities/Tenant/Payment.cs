using ROCloud.Domain.Entities.Common;
using ROCloud.Domain.Enums;

namespace ROCloud.Domain.Entities.Tenant;

/// <summary>
/// A payment from a customer. DB table: payments. The table has no
/// updated_at/is_deleted columns — Phase 3 ignores those BaseEntity members.
/// </summary>
public class Payment : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid? InvoiceId { get; set; }
    public Guid? OrderId { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public PaymentPreference? PaymentPreference { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Completed;
    public string? ReferenceNumber { get; set; }
    public string? RazorpayPaymentId { get; set; }
    public Guid? CollectedBy { get; set; }
    public DateTime PaidAt { get; set; }
    public string? Notes { get; set; }

    // Navigation
    public Customer? Customer { get; set; }
    public Invoice? Invoice { get; set; }
}
