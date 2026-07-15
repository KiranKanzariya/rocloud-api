using ROCloud.Application.Features.Subscription.Dtos;
using ROCloud.Domain.Entities.Platform;

namespace ROCloud.Application.Features.Subscription.Services;

/// <summary>
/// Builds the subscription-invoice PDF model from the invoice row + its tenant. Shared by the delivery
/// service (which attaches the PDF to the owner's email) and the download endpoint (which re-renders it
/// on every request) — the PDF is never stored, so this is the single definition of what it contains.
/// </summary>
public static class SubscriptionInvoicePdfModelBuilder
{
    public static SubscriptionInvoicePdfModel Build(SubscriptionInvoice invoice, Tenant tenant, bool paid)
    {
        // CreatedAt is stamped on save, so a not-yet-persisted invoice (the email path renders before
        // SaveChanges) still dates as today; a stored row always re-renders with its original date.
        var issuedOn = invoice.CreatedAt == default ? DateTime.UtcNow : invoice.CreatedAt;

        return new SubscriptionInvoicePdfModel(
            invoice.InvoiceNumber,
            DateOnly.FromDateTime(issuedOn),
            invoice.PeriodStart, invoice.PeriodEnd,
            invoice.PlanType, invoice.BillingCycle,
            invoice.Description ?? $"{invoice.PlanType} plan",
            invoice.GrossAmount, invoice.DiscountAmount, invoice.Amount,
            paid, tenant.Name, tenant.GstNumber);
    }
}
