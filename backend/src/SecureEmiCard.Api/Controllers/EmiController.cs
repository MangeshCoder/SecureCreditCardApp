using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureEmiCard.Api.Auditing;
using SecureEmiCard.Application.Features.Emi;
using SecureEmiCard.Domain.Common;

namespace SecureEmiCard.Api.Controllers;

/// <summary>
/// EMI management and conversion (spec §4C EmiController), completed:
/// calculator, eligible purchases, conversion, plans with amortization schedules and installment payment.
/// </summary>
[ApiController]
[Route("api/emi")]
[Authorize]
public class EmiController : ControllerBase
{
    private readonly IEmiService _emi;

    public EmiController(IEmiService emi) => _emi = emi;

    /// <summary>Bank's EMI rules: minimum amount, conversion window, interest rate per tenure.</summary>
    [HttpGet("rules")]
    public ActionResult<EmiRulesDto> GetRules() => Ok(_emi.GetRules());

    /// <summary>EMI calculator: full schedule for an amount and tenure (the rate is set by the bank).</summary>
    [HttpPost("calculate-preview")]
    public ActionResult<EmiCalculationDto> PreviewEmiPlan(EmiPreviewRequest request) => Ok(_emi.Preview(request));

    /// <summary>All tenure options for an amount (monthly installment, interest, total) - for the "choose tenure" table.</summary>
    [HttpGet("options")]
    public ActionResult<IReadOnlyList<EmiOptionDto>> GetOptions([FromQuery] decimal amount) => Ok(_emi.GetOptions(amount));

    /// <summary>Purchases of a card that can be converted to EMI right now. Owner or Admin.</summary>
    [HttpGet("eligible/card/{cardId:int}")]
    public async Task<ActionResult<IReadOnlyList<EligibleTransactionDto>>> GetEligible(int cardId, CancellationToken ct)
        => Ok(await _emi.GetEligibleTransactionsAsync(cardId, ct));

    /// <summary>Converts a purchase into an EMI plan. Owner or Admin.</summary>
    [HttpPost("convert-transaction/{transactionId:int}")]
    [Audit(AuditActions.EmiConversion)]
    [ProducesResponseType<EmiPlanDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<EmiPlanDto>> ConvertToEmi(int transactionId, ConvertToEmiRequest request, CancellationToken ct)
    {
        var plan = await _emi.ConvertTransactionAsync(transactionId, request, ct);
        return CreatedAtAction(nameof(GetPlan), new { emiPlanId = plan.EmiPlanId }, plan);
    }

    /// <summary>EMI plans of a card (with schedules), newest first. Owner or Admin.</summary>
    [HttpGet("plans/card/{cardId:int}")]
    public async Task<ActionResult<IReadOnlyList<EmiPlanDto>>> GetCardPlans(int cardId, CancellationToken ct)
        => Ok(await _emi.GetCardPlansAsync(cardId, ct));

    /// <summary>EMI position of a card: principal in EMI, remaining, next due, and how much "Pay bill" may pay.</summary>
    [HttpGet("summary/card/{cardId:int}")]
    public async Task<ActionResult<CardEmiSummaryDto>> GetCardSummary(int cardId, CancellationToken ct)
        => Ok(await _emi.GetCardSummaryAsync(cardId, ct));

    [HttpGet("plans/{emiPlanId:int}")]
    public async Task<ActionResult<EmiPlanDto>> GetPlan(int emiPlanId, CancellationToken ct)
        => Ok(await _emi.GetPlanAsync(emiPlanId, ct));

    /// <summary>
    /// Pays installment N. The number is part of the URL on purpose: repeating the same request
    /// (double click, network retry) answers "already paid" instead of paying the next installment.
    /// </summary>
    [HttpPost("plans/{emiPlanId:int}/installments/{installmentNumber:int}/pay")]
    public async Task<ActionResult<PayInstallmentResponse>> PayInstallment(int emiPlanId, int installmentNumber, CancellationToken ct)
        => Ok(await _emi.PayInstallmentAsync(emiPlanId, installmentNumber, ct));
}
