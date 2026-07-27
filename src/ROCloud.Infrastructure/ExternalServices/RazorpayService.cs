using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ROCloud.Application.Common.Interfaces;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Infrastructure.ExternalServices;

/// <summary>
/// Razorpay REST integration via raw HttpClient + HMAC (guide §10). No card data ever
/// touches our system — Razorpay holds PCI scope (§10.18). Webhook signatures are verified
/// with the configured webhook secret (falling back to the API key secret).
/// </summary>
public class RazorpayService : IRazorpayService
{
    private const string DefaultBaseUrl = "https://api.razorpay.com/v1/";

    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<RazorpayService> _logger;

    public RazorpayService(HttpClient http, IConfiguration config, ILogger<RazorpayService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    private string BaseUrl => _config["Razorpay:BaseUrl"] is { Length: > 0 } u ? u : DefaultBaseUrl;

    private string KeyId => _config["Razorpay:KeyId"] ?? string.Empty;
    private string KeySecret => _config["Razorpay:KeySecret"] ?? string.Empty;
    private string WebhookSecret =>
        _config["Razorpay:WebhookSecret"] is { Length: > 0 } ws ? ws : KeySecret;

    public bool IsConfigured =>
        KeyId is { Length: > 0 } && KeySecret is { Length: > 0 }
        && !KeyId.Contains("xxx", StringComparison.OrdinalIgnoreCase)
        && !KeyId.StartsWith("your-", StringComparison.OrdinalIgnoreCase);

    public string PublicKeyId => KeyId;
    public string Currency => _config["Razorpay:Currency"] is { Length: > 0 } c ? c : "INR";
    private int SubscriptionTotalCount =>
        int.TryParse(_config["Razorpay:SubscriptionTotalCount"], out var n) ? n : 12;

    /// <summary>Razorpay rejects an order whose receipt exceeds this many characters.</summary>
    private const int MaxReceiptLength = 40;

    public async Task<RazorpayOrder> CreateOrderAsync(long amountPaise, string receipt, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["razorpay"] = ["Online payments are not configured (missing Razorpay credentials)."]
            });

        // The receipt is a free-text reference shown in the Razorpay dashboard — worth truncating rather
        // than failing the whole payment, since an over-long one 400s the order with a message the caller
        // can do nothing about. Callers should stay inside the limit; this is the backstop.
        if (receipt.Length > MaxReceiptLength)
        {
            _logger.LogWarning(
                "Razorpay receipt '{Receipt}' is {Length} chars — truncating to {Max}.",
                receipt, receipt.Length, MaxReceiptLength);
            receipt = receipt[..MaxReceiptLength];
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}orders")
        {
            Content = JsonContent.Create(new
            {
                amount = amountPaise,
                currency = Currency,
                receipt,
                payment_capture = 1
            })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{KeyId}:{KeySecret}")));

        using var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogError("Razorpay order creation failed ({Status}): {Body}", resp.StatusCode, body);
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["razorpay"] = ["Failed to create the Razorpay order."]
            });
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var orderId = root.GetProperty("id").GetString()!;
        var amount = root.GetProperty("amount").GetInt64();
        return new RazorpayOrder(orderId, amount, Currency, KeyId);
    }

    // NOTE: live Razorpay call — verify against test keys before relying on it in production.
    public async Task<RazorpayPaymentStatus> GetOrderPaymentStatusAsync(string orderId, CancellationToken ct = default)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(orderId))
            return new RazorpayPaymentStatus(false, null);

        // 1. Is the order paid?
        using var orderResp = await SendAuthedGetAsync($"{BaseUrl}orders/{orderId}", ct);
        var orderBody = await orderResp.Content.ReadAsStringAsync(ct);
        if (!orderResp.IsSuccessStatusCode)
        {
            _logger.LogWarning("Razorpay order fetch failed ({Status}) for {OrderId}: {Body}", orderResp.StatusCode, orderId, orderBody);
            return new RazorpayPaymentStatus(false, null);
        }

        using var orderDoc = JsonDocument.Parse(orderBody);
        var status = orderDoc.RootElement.TryGetProperty("status", out var s) ? s.GetString() : null;
        if (!string.Equals(status, "paid", StringComparison.OrdinalIgnoreCase))
            return new RazorpayPaymentStatus(false, null);

        // 2. Find the captured payment for this order — its id, and how it was paid.
        string? capturedId = null;
        string? method = null;
        string? instrument = null;
        using var payResp = await SendAuthedGetAsync($"{BaseUrl}orders/{orderId}/payments", ct);
        if (payResp.IsSuccessStatusCode)
        {
            using var payDoc = JsonDocument.Parse(await payResp.Content.ReadAsStringAsync(ct));
            if (payDoc.RootElement.TryGetProperty("items", out var items))
                foreach (var item in items.EnumerateArray())
                {
                    var st = item.TryGetProperty("status", out var ps) ? ps.GetString() : null;
                    if (string.Equals(st, "captured", StringComparison.OrdinalIgnoreCase))
                    {
                        capturedId = item.GetProperty("id").GetString();
                        method = item.TryGetProperty("method", out var m) ? m.GetString() : null;
                        instrument = DescribeInstrument(item, method);
                        break;
                    }
                }
        }

        return new RazorpayPaymentStatus(true, capturedId, method, instrument);
    }

    /// <summary>
    /// Renders the payment's instrument for display: the UPI id, "Visa •••• 4366", the bank, or the
    /// wallet. Composed here so both billing screens describe a charge identically, and so the DB never
    /// holds anything beyond what Razorpay already exposes (last four digits at most — §10.18).
    /// Returns null when the payload doesn't carry the expected detail, which the UI renders as bare method.
    /// </summary>
    private static string? DescribeInstrument(JsonElement payment, string? method)
    {
        static string? Str(JsonElement el, string prop) =>
            el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() is { Length: > 0 } s ? s : null
                : null;

        switch (method?.ToLowerInvariant())
        {
            case "card":
                if (!payment.TryGetProperty("card", out var card) || card.ValueKind != JsonValueKind.Object)
                    return null;
                var last4 = Str(card, "last4");
                var network = Str(card, "network");
                if (last4 is null) return network;
                return network is null ? $"•••• {last4}" : $"{network} •••• {last4}";

            case "upi":
                // Razorpay puts the payer's VPA at the top level; newer payloads also nest it under `upi`.
                return Str(payment, "vpa")
                       ?? (payment.TryGetProperty("upi", out var upi) && upi.ValueKind == JsonValueKind.Object
                           ? Str(upi, "vpa") : null);

            case "netbanking":
                return Str(payment, "bank");

            case "wallet":
                return Str(payment, "wallet");

            default:
                return null;
        }
    }

    private async Task<HttpResponseMessage> SendAuthedGetAsync(string url, CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{KeyId}:{KeySecret}")));
        return await _http.SendAsync(req, ct);
    }

    public bool VerifyWebhookSignature(string rawBody, string? signature)
    {
        if (string.IsNullOrEmpty(signature) || WebhookSecret.Length == 0)
            return false;

        var expected = ComputeHmacHex(rawBody, WebhookSecret);
        var a = Encoding.UTF8.GetBytes(expected);
        var b = Encoding.UTF8.GetBytes(signature);
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    public async Task<string> CreateSubscriptionAsync(string planId, string customerId, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["razorpay"] = ["Subscription billing is not configured (missing Razorpay credentials)."]
            });

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}subscriptions")
        {
            Content = JsonContent.Create(new
            {
                plan_id = planId,
                customer_id = customerId,
                total_count = SubscriptionTotalCount   // Razorpay:SubscriptionTotalCount (default 12)
            })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{KeyId}:{KeySecret}")));

        using var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogError("Razorpay subscription creation failed ({Status}): {Body}", resp.StatusCode, body);
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["razorpay"] = ["Failed to create the Razorpay subscription."]
            });
        }

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    public async Task CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default)
    {
        if (!IsConfigured) return;

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}subscriptions/{subscriptionId}/cancel");
        req.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{KeyId}:{KeySecret}")));

        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogError("Razorpay subscription cancel failed ({Status}): {Body}", resp.StatusCode, body);
        }
    }

    private static string ComputeHmacHex(string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexStringLower(hash);
    }
}
