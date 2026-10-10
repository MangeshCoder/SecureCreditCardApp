using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureEmiCard.Api.Auditing;
using SecureEmiCard.Application.Features.Billing;
using SecureEmiCard.Domain.Common;

namespace SecureEmiCard.Api.Controllers;

/// <summary>
/// Module 8 - billing cycle and statements. Cardholders see their own cards; admins see every card and can
/// close a billing cycle on demand (the scheduler does it automatically every cycle).
/// </summary>
[ApiController]
[Route("api/billing")]
[Authorize]
public class BillingController : ControllerBase
{
    private readonly IBillingService _billing;

    public BillingController(IBillingService billing) => _billing = billing;

    /// <summary>The bank's billing terms: minimum due, interest, fees, GST, late fee slabs.</summary>
    [HttpGet("rules")]
    public ActionResult<BillingRulesDto> GetRules() => Ok(_billing.GetRules());

    /// <summary>"Your bill": last statement, paid since, what is left, overdue or not, unbilled spend.</summary>
    [HttpGet("cards/{cardId:int}/summary")]
    public async Task<ActionResult<CardBillingSummaryDto>> GetSummary(int cardId, CancellationToken ct)
        => Ok(await _billing.GetSummaryAsync(cardId, ct));

    /// <summary>All statements of a card, newest first.</summary>
    [HttpGet("cards/{cardId:int}/statements")]
    public async Task<ActionResult<IReadOnlyList<StatementDto>>> GetStatements(int cardId, CancellationToken ct)
        => Ok(await _billing.GetStatementsAsync(cardId, ct));

    /// <summary>Back office: close the billing cycle now and generate the statement.</summary>
    [HttpPost("cards/{cardId:int}/statements")]
    [Authorize(Roles = "Admin")]
    [Audit(AuditActions.StatementGenerated)]
    [ProducesResponseType<StatementDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<StatementDto>> Generate(int cardId, CancellationToken ct)
    {
        var statement = await _billing.GenerateStatementAsync(cardId, ct);
        return CreatedAtAction(nameof(GetStatement), new { statementId = statement.StatementId }, statement);
    }

    /// <summary>One statement with all its lines.</summary>
    [HttpGet("statements/{statementId:int}")]
    public async Task<ActionResult<StatementDetailDto>> GetStatement(int statementId, CancellationToken ct)
        => Ok(await _billing.GetStatementAsync(statementId, ct));

    /// <summary>The statement as a PDF file.</summary>
    [HttpGet("statements/{statementId:int}/pdf")]
    [Produces("application/pdf")]
    public async Task<IActionResult> GetPdf(int statementId, CancellationToken ct)
    {
        var pdf = await _billing.GetStatementPdfAsync(statementId, ct);
        return File(pdf.Content, "application/pdf", pdf.FileName);
    }
}
