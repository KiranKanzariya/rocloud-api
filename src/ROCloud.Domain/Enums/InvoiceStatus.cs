namespace ROCloud.Domain.Enums;

/// <summary>Invoice lifecycle state. DB: invoices.status.</summary>
public enum InvoiceStatus
{
    Draft,
    Sent,
    Paid,
    PartiallyPaid,
    Overdue,
    Cancelled
}
