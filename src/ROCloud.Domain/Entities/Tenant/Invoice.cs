using ROCloud.Domain.Entities.Common;
using ROCloud.Domain.Enums;

namespace ROCloud.Domain.Entities.Tenant;

/// <summary>A customer invoice. DB table: invoices.</summary>
public class Invoice : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid CustomerId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public DateOnly DueDate { get; set; }
    public DateOnly? PeriodFrom { get; set; }
    public DateOnly? PeriodTo { get; set; }
    public decimal SubTotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Discount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;
    public string? GstNumber { get; set; }
    public string? Notes { get; set; }

    // Navigation
    public Customer? Customer { get; set; }
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}
