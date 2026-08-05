using ROCloud.Application.Common.Interfaces;

namespace ROCloud.Application.Tests;

/// <summary>Configurable IRazorpayService fake for tests. Unconfigured by default (payment skipped).</summary>
public sealed class FakeRazorpayService : IRazorpayService
{
    public bool Configured { get; init; }
    public string CreatedOrderId { get; init; } = "order_test";

    /// <summary>Order id → status returned by GetOrderPaymentStatusAsync (unknown ⇒ not paid).</summary>
    public Dictionary<string, RazorpayPaymentStatus> PaidStatuses { get; } = new();

    public bool IsConfigured => Configured;
    public string PublicKeyId => "key_test";
    public string Currency => "INR";

    /// <summary>Receipt passed to the last CreateOrderAsync call — Razorpay caps it at 40 chars.</summary>
    public string? LastReceipt { get; private set; }

    public Task<RazorpayOrder> CreateOrderAsync(long amountPaise, string receipt, CancellationToken ct = default)
    {
        LastReceipt = receipt;
        return Task.FromResult(new RazorpayOrder(CreatedOrderId, amountPaise, "INR", "key_test"));
    }

    public Task<RazorpayPaymentStatus> GetOrderPaymentStatusAsync(string orderId, CancellationToken ct = default)
        => Task.FromResult(PaidStatuses.TryGetValue(orderId, out var s) ? s : new RazorpayPaymentStatus(false, null));

    /// <summary>VPA → result for ValidateVpaAsync. An id not listed comes back Unavailable, matching
    /// the real service's behaviour when it cannot reach Razorpay or has no credentials.</summary>
    public Dictionary<string, RazorpayVpaValidation> VpaResults { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<RazorpayVpaValidation> ValidateVpaAsync(string vpa, CancellationToken ct = default)
        => Task.FromResult(VpaResults.TryGetValue(vpa, out var r)
            ? r
            : new RazorpayVpaValidation(false, null, Unavailable: true));

    public bool VerifyWebhookSignature(string rawBody, string? signature) => true;
    public Task<string> CreateSubscriptionAsync(string planId, string customerId, CancellationToken ct = default) => Task.FromResult("sub_test");
    public Task CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default) => Task.CompletedTask;
}
