using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureEmiCard.Application.Common.Models;
using SecureEmiCard.Application.Features.Cashback;

namespace SecureEmiCard.Api.Controllers;

/// <summary>
/// Cashback rewards: the active rules, per-card totals and the cashback ledger.
/// Cashback itself is created automatically by approved swipes (and reversed by refunds).
/// </summary>
[ApiController]
[Route("api/cashback")]
[Authorize]
public class CashbackController : ControllerBase
{
    private readonly ICashbackService _cashback;

    public CashbackController(ICashbackService cashback) => _cashback = cashback;

    /// <summary>Current cashback rules (percentage per merchant category, minimum spend, cap).</summary>
    [HttpGet("rules")]
    public ActionResult<CashbackRulesDto> GetRules() => Ok(_cashback.GetRules());

    /// <summary>Totals for one card: earned, reversed, net, this month, and per merchant category. Owner or Admin.</summary>
    [HttpGet("card/{cardId:int}/summary")]
    public async Task<ActionResult<CashbackSummaryDto>> GetSummary(int cardId, CancellationToken ct)
        => Ok(await _cashback.GetCardSummaryAsync(cardId, ct));

    /// <summary>Cashback ledger of one card, newest first. Owner or Admin.</summary>
    [HttpGet("card/{cardId:int}")]
    public async Task<ActionResult<PagedResult<CashbackLogDto>>> GetHistory(
        int cardId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await _cashback.GetCardHistoryAsync(cardId, page, pageSize, ct));
}
