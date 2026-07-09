namespace ROCloud.Domain.Enums;

/// <summary>How a customer prefers to be billed. DB: customers.payment_preference, payments.payment_preference.</summary>
public enum PaymentPreference
{
    PerBottle,
    Weekly,
    Monthly,
    Combined
}
