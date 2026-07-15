using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Entities.Platform;

namespace ROCloud.Application.Tests;

/// <summary>No-op ISubscriptionInvoiceDelivery for handler unit tests (PDF/email is out of scope there).</summary>
public sealed class NoOpSubscriptionInvoiceDelivery : ISubscriptionInvoiceDelivery
{
    public Task IssueAsync(SubscriptionInvoice invoice, Tenant tenant, CancellationToken ct = default) => Task.CompletedTask;
    public Task ReceiptAsync(SubscriptionInvoice invoice, Tenant tenant, CancellationToken ct = default) => Task.CompletedTask;
}
