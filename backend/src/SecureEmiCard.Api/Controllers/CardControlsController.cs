using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureEmiCard.Api.Auditing;
using SecureEmiCard.Application.Features.CardControls;
using SecureEmiCard.Application.Features.Cards;
using SecureEmiCard.Domain.Common;

namespace SecureEmiCard.Api.Controllers;

/// <summary>
/// Module 6 - card controls: channel switches, daily limits and the temporary lock.
/// Reading is allowed for the owner and admins; changing only for the cardholder (checked in CardControlService).
/// </summary>
[ApiController]
[Route("api/cards/{cardId:int}")]
[Authorize]
public class CardControlsController : ControllerBase
{
    private readonly ICardControlService _controls;

    public CardControlsController(ICardControlService controls) => _controls = controls;

    /// <summary>Switches and limits, with what was already spent today per channel.</summary>
    [HttpGet("controls")]
    public async Task<ActionResult<CardControlsDto>> Get(int cardId, CancellationToken ct)
        => Ok(await _controls.GetAsync(cardId, ct));

    /// <summary>Replaces all switches and daily limits. Takes effect for the very next transaction.</summary>
    [HttpPut("controls")]
    [Audit(AuditActions.CardControlsChanged)]
    public async Task<ActionResult<CardControlsDto>> Update(int cardId, UpdateCardControlsRequest request, CancellationToken ct)
    {
        var controls = await _controls.UpdateAsync(cardId, request, ct);
        HttpContext.SetAuditDetail(Summary(controls)); // the audit row shows WHAT was switched on, not just "changed"
        return Ok(controls);
    }

    /// <summary>Temporary lock by the cardholder. Every purchase is declined until they unlock it.</summary>
    [HttpPost("lock")]
    [Audit(AuditActions.CardLocked)]
    public async Task<ActionResult<CardDto>> Lock(int cardId, CancellationToken ct)
        => Ok(await _controls.LockAsync(cardId, ct));

    [HttpPost("unlock")]
    [Audit(AuditActions.CardUnlocked)]
    public async Task<ActionResult<CardDto>> Unlock(int cardId, CancellationToken ct)
        => Ok(await _controls.UnlockAsync(cardId, ct));

    private static string Summary(CardControlsDto c)
    {
        static string Part(string name, ChannelControlDto s) =>
            $"{name} {(s.Enabled ? "on" : "off")}{(s.DailyLimit is { } limit ? $" (limit {limit:0.00})" : "")}";

        return string.Join(" · ", Part("POS", c.Pos), Part("Online", c.Online), Part("Contactless", c.Contactless),
                                  Part("ATM", c.Atm), Part("International", c.International));
    }
}
