namespace ROCloud.Domain.Entities.Platform;

/// <summary>
/// Maps a Razorpay order id → its tenant + local payment. Deliberately NOT tenant-scoped and NOT
/// RLS-protected (it is not an ITenantEntity and its table has no RLS policy), so the anonymous
/// Razorpay webhook — which has no tenant context — can resolve the tenant BEFORE reading the
/// RLS-protected payments row. Written at payment-initiation time. DB table: razorpay_order_index.
/// </summary>
public class RazorpayOrderIndex
{
    public string RazorpayOrderId { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public Guid PaymentId { get; set; }
    public DateTime CreatedAt { get; set; }
}
