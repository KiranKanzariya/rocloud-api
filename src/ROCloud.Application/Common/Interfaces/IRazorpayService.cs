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
/// Outcome of checking a UPI id (VPA) against the payments network.
/// </summary>
/// <param name="Valid">
/// True only when the network confirmed the id EXISTS. It does not prove the id belongs to the person
/// asking — that is what <paramref name="PayeeName"/> is for: the owner reads the registered name back
/// and confirms it is their own account.
/// </param>
/// <param name="PayeeName">The account name the id is registered to, when the network returns one.</param>
/// <param name="Unavailable">
/// True when the check could not be RUN (no credentials, network down, endpoint not enabled on the
/// account) — distinct from a definitive "this id does not exist". Conflating the two would tell an
/// owner their perfectly good UPI id is wrong.
/// </param>
public sealed record RazorpayVpaValidation(bool Valid, string? PayeeName, bool Unavailable = false);

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

    /// <summary>
    /// Asks Razorpay whether a UPI id exists, and for the name it is registered to — used by the
    /// owner's "Verify" button before a scan-to-pay QR goes onto customer invoices. Never throws for
    /// an unusable id or an unreachable API: returns <c>Unavailable</c> so the caller can say why.
    /// </summary>
    Task<RazorpayVpaValidation> ValidateVpaAsync(string vpa, CancellationToken ct = default);

    /// <summary>Creates a Razorpay subscription (ROCloud tenant billing — used from Phase 25).</summary>
    Task<string> CreateSubscriptionAsync(string planId, string customerId, CancellationToken ct = default);

    /// <summary>Cancels a Razorpay subscription.</summary>
    Task CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default);
}
