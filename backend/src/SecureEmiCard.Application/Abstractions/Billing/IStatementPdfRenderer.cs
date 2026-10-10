using SecureEmiCard.Application.Features.Billing;

namespace SecureEmiCard.Application.Abstractions.Billing;

/// <summary>Module 8: turns a statement into a PDF (implemented with QuestPDF in Infrastructure).</summary>
public interface IStatementPdfRenderer
{
    byte[] Render(StatementDetailDto statement);
}
