namespace ROCloud.Domain.Enums;

/// <summary>Payment processing state. DB: payments.status.</summary>
public enum PaymentStatus
{
    Pending,
    Completed,
    Failed,
    Refunded
}
