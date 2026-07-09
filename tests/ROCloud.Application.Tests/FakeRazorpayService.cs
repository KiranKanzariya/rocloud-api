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

    public Task<RazorpayOrder> CreateOrderAsync(long amountPaise, string receipt, CancellationToken ct = default)
        => Task.FromResult(new RazorpayOrder(CreatedOrderId, amountPaise, "INR", "key_test"));

    public Task<RazorpayPaymentStatus> GetOrderPaymentStatusAsync(string orderId, CancellationToken ct = default)
        => Task.FromResult(PaidStatuses.TryGetValue(orderId, out var s) ? s : new RazorpayPaymentStatus(false, null));

    public bool VerifyWebhookSignature(string rawBody, string? signature) => true;
    public Task<string> CreateSubscriptionAsync(string planId, string customerId, CancellationToken ct = default) => Task.FromResult("sub_test");
    public Task CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default) => Task.CompletedTask;
}
