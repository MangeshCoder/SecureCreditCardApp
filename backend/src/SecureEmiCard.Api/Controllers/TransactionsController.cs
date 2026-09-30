using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SecureEmiCard.Application.Common.Models;
using SecureEmiCard.Application.Features.Transactions;

namespace SecureEmiCard.Api.Controllers;

/// <summary>
/// Merchant swipe authorization, balance loads (repayments), refunds and the transaction ledger.
/// </summary>
[ApiController]
[Route("api/transactions")]
[Authorize]
public class TransactionsController : ControllerBase
{
    private readonly ITransactionService _transactions;

    public TransactionsController(ITransactionService transactions) => _transactions = transactions;

    /// <summary>
    /// Authorizes a purchase (POS terminal / checkout simulator). Returns 200 for both approved and
    /// declined swipes - check "approved" in the body. Cardholders may only use their own cards.
    /// </summary>
    [HttpPost("swipe")]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    public async Task<ActionResult<SwipeResponse>> Swipe(SwipeRequest request, CancellationToken ct)
        => Ok(await _transactions.SwipeAsync(request, ct));

    /// <summary>Repayment / balance load. Increases the available balance (up to the credit limit).</summary>
    [HttpPost("load")]
    public async Task<ActionResult<BalanceChangeResponse>> Load(LoadRequest request, CancellationToken ct)
        => Ok(await _transactions.LoadAsync(request, ct));

    /// <summary>Full refund of a completed swipe (merchant / bank back office).</summary>
    [HttpPost("{transactionId:int}/refund")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<BalanceChangeResponse>> Refund(int transactionId, CancellationToken ct)
        => Ok(await _transactions.RefundAsync(transactionId, ct));

    /// <summary>Ledger of one card, newest first. Owner or Admin.</summary>
    [HttpGet("card/{cardId:int}")]
    public async Task<ActionResult<PagedResult<TransactionDto>>> GetForCard(
        int cardId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await _transactions.GetCardTransactionsAsync(cardId, page, pageSize, ct));

    /// <summary>All transactions across all cards, newest first.</summary>
    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<PagedResult<TransactionDto>>> GetAll(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await _transactions.GetAllTransactionsAsync(page, pageSize, ct));

    /// <summary>Merchant categories (MCC) for the checkout simulator drop-down.</summary>
    [HttpGet("merchant-categories")]
    public ActionResult<IReadOnlyList<MerchantCategoryDto>> GetMerchantCategories()
        => Ok(_transactions.GetMerchantCategories());
}
