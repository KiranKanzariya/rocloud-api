using ROCloud.Application.Features.Invoices.Dtos;

namespace ROCloud.Application.Common.Interfaces;

/// <summary>Renders a GST-compliant invoice PDF (guide §10). Implemented in Infrastructure via QuestPDF.</summary>
public interface IInvoicePdfGenerator
{
    byte[] Generate(InvoicePdfModel model);
}
