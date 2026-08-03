using ROCloud.Application.Features.Statements.Dtos;

namespace ROCloud.Application.Common.Interfaces;

/// <summary>Renders a customer's delivery statement (proof of supply) to PDF bytes.</summary>
public interface IStatementPdfGenerator
{
    byte[] Generate(StatementPdfModel model);
}
