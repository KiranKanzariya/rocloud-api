using ROCloud.Application.Features.Subscription.Dtos;

namespace ROCloud.Application.Common.Interfaces;

/// <summary>Renders a ROCloud subscription-invoice PDF (guide §25/§26). QuestPDF-backed in Infrastructure.</summary>
public interface ISubscriptionInvoicePdfGenerator
{
    byte[] Generate(SubscriptionInvoicePdfModel model);
}
