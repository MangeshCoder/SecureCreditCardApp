using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SecureEmiCard.Api.Auditing;
using SecureEmiCard.Application.Features.Cards;
using SecureEmiCard.Domain.Common;

namespace SecureEmiCard.Api.Controllers;

/// <summary>
/// Card lifecycle: issuance, status control (Active/Blocked), credit limit, PIN management.
/// Role checks happen here ([Authorize(Roles = ...)]) AND in CardService (defence in depth),
/// and CardService also enforces that cardholders can only touch their own cards.
/// </summary>
[ApiController]
[Route("api/cards")]
[Authorize]
public class CardsController : ControllerBase
{
    private readonly ICardService _cards;

    public CardsController(ICardService cards) => _cards = cards;

    /// <summary>Cards of the logged-in cardholder.</summary>
    [HttpGet("my")]
    public async Task<ActionResult<IReadOnlyList<CardDto>>> GetMyCards(CancellationToken ct)
        => Ok(await _cards.GetMyCardsAsync(ct));

    /// <summary>All cards in the system (bank back-office).</summary>
    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<IReadOnlyList<CardDto>>> GetAll(CancellationToken ct)
        => Ok(await _cards.GetAllCardsAsync(ct));

    [HttpGet("{cardId:int}")]
    public async Task<ActionResult<CardDto>> Get(int cardId, CancellationToken ct)
        => Ok(await _cards.GetCardAsync(cardId, ct));

    /// <summary>Issues a new card. The response contains the full number, CVV and initial PIN exactly once.</summary>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    [Audit(AuditActions.CardIssued)]
    [ProducesResponseType<IssuedCardResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<IssuedCardResponse>> Issue(IssueCardRequest request, CancellationToken ct)
    {
        var issued = await _cards.IssueCardAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { cardId = issued.Card.CardId }, issued);
    }

    [HttpPost("{cardId:int}/block")]
    [Audit(AuditActions.CardBlocked)]
    public async Task<ActionResult<CardDto>> Block(int cardId, CancellationToken ct)
        => Ok(await _cards.BlockCardAsync(cardId, ct));

    [HttpPost("{cardId:int}/activate")]
    [Authorize(Roles = "Admin")]
    [Audit(AuditActions.CardUnblocked)]
    public async Task<ActionResult<CardDto>> Activate(int cardId, CancellationToken ct)
        => Ok(await _cards.ActivateCardAsync(cardId, ct));

    [HttpPut("{cardId:int}/credit-limit")]
    [Authorize(Roles = "Admin")]
    [Audit(AuditActions.CreditLimitChanged)]
    public async Task<ActionResult<CardDto>> UpdateCreditLimit(int cardId, UpdateCreditLimitRequest request, CancellationToken ct)
        => Ok(await _cards.UpdateCreditLimitAsync(cardId, request, ct));

    [HttpPut("{cardId:int}/pin")]
    [Audit(AuditActions.PinChanged)]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    public async Task<IActionResult> ChangePin(int cardId, ChangePinRequest request, CancellationToken ct)
    {
        await _cards.ChangePinAsync(cardId, request, ct);
        return NoContent();
    }

    /// <summary>Shows the full (decrypted) card number to its owner after PIN verification.</summary>
    [HttpPost("{cardId:int}/reveal")]
    [Audit(AuditActions.CardNumberRevealed)]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    public async Task<ActionResult<RevealCardNumberResponse>> Reveal(int cardId, RevealCardNumberRequest request, CancellationToken ct)
        => Ok(await _cards.RevealCardNumberAsync(cardId, request, ct));
}
