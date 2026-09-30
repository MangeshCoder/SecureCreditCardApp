using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureEmiCard.Application.Features.Cardholders;
using SecureEmiCard.Application.Features.Cards;

namespace SecureEmiCard.Api.Controllers;

/// <summary>Bank back-office endpoints for managing customers. Admin only.</summary>
[ApiController]
[Route("api/cardholders")]
[Authorize(Roles = "Admin")]
public class CardholdersController : ControllerBase
{
    private readonly ICardholderService _cardholders;
    private readonly ICardService _cards;

    public CardholdersController(ICardholderService cardholders, ICardService cards)
    {
        _cardholders = cardholders;
        _cards = cards;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CardholderDto>>> GetAll(CancellationToken ct)
        => Ok(await _cardholders.GetAllAsync(ct));

    [HttpGet("{cardholderId:int}")]
    public async Task<ActionResult<CardholderDto>> Get(int cardholderId, CancellationToken ct)
        => Ok(await _cardholders.GetByIdAsync(cardholderId, ct));

    [HttpGet("{cardholderId:int}/cards")]
    public async Task<ActionResult<IReadOnlyList<CardDto>>> GetCards(int cardholderId, CancellationToken ct)
        => Ok(await _cards.GetCardsOfCardholderAsync(cardholderId, ct));

    [HttpPost("{cardholderId:int}/deactivate")]
    public async Task<IActionResult> Deactivate(int cardholderId, CancellationToken ct)
    {
        await _cardholders.SetActiveAsync(cardholderId, false, ct);
        return NoContent();
    }

    [HttpPost("{cardholderId:int}/activate")]
    public async Task<IActionResult> Activate(int cardholderId, CancellationToken ct)
    {
        await _cardholders.SetActiveAsync(cardholderId, true, ct);
        return NoContent();
    }
}
