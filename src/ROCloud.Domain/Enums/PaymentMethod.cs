namespace ROCloud.Domain.Enums;

/// <summary>
/// How a payment was made. Union of the two DB CHECK sets:
/// deliveries.payment_method allows Cash/UPI/Card/Online/None;
/// payments.payment_method allows Cash/UPI/Card/Online/BankTransfer.
/// The per-column CHECK still enforces the valid subset.
/// </summary>
public enum PaymentMethod
{
    Cash,
    UPI,
    Card,
    Online,
    BankTransfer,
    None
}
