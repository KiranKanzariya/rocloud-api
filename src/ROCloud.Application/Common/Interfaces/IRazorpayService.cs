namespace ROCloud.Application.Common.Interfaces;

/// <summary>Razorpay order/subscription created via the REST API.</summary>
public sealed record RazorpayOrder(string OrderId, long AmountPaise, string Currency, string KeyId);

/// <summary>
/// Whether a Razorpay order has been paid, the captured payment id, and how it was paid.
/// <paramref name="Method"/> is Razorpay's raw method ("card", "upi", "netbanking", "wallet");
/// <paramref name="Instrument"/> is the display detail for that method — the UPI id, "Visa •••• 4366",
/// the bank, or the wallet name. Both are null when the payment predates capture or is unknown.
/// Never contains a full card number: Razorpay only ever exposes the last four digits (§10.18).
/// </summary>
public sealed record RazorpayPaymentStatus(
    bool Paid, string? PaymentId, string? Method = null, string? Instrument = null);

/// <summary>
/// Razorpay integration (guide §10). Online payments + ROCloud's own subscription billing.
/// PCI scope stays with Razorpay — we never see or store card data (§10.18).
/// </summary>
public interface IRazorpayService
{
    /// <summary>True when usable live Razorpay credentials are configured (false in dev / placeholder keys).</summary>
    bool IsConfigured { get; }

    /// <summary>The public Razorpay key id for the client Checkout widget (empty when unconfigured).</summary>
    string PublicKeyId { get; }

    /// <summary>Billing currency (Razorpay:Currency, default INR).</summary>
    string Currency { get; }

    /// <summary>Creates a Razorpay order. <paramref name="amountPaise"/> is in the smallest unit (paise).</summary>
    Task<RazorpayOrder> CreateOrderAsync(long amountPaise, string receipt, CancellationToken ct = default);

    /// <summary>Fetches whether a Razorpay order was paid (and the captured payment id) — used by the
    /// reconciliation job to resolve local payments stuck in Pending. Returns Paid=false when unconfigured.</summary>
    Task<RazorpayPaymentStatus> GetOrderPaymentStatusAsync(string orderId, CancellationToken ct = default);

    /// <summary>
    /// Constant-time verification of a webhook signature: HMAC-SHA256(rawBody, webhook secret)
    /// compared to the X-Razorpay-Signature header. Returns false when no secret is configured.
    /// </summary>
    bool VerifyWebhookSignature(string rawBody, string? signature);

    /// <summary>Creates a Razorpay subscription (ROCloud tenant billing — used from Phase 25).</summary>
    Task<string> CreateSubscriptionAsync(string planId, string customerId, CancellationToken ct = default);

    /// <summary>Cancels a Razorpay subscription.</summary>
    Task CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default);
}
