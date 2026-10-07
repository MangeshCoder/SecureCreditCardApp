using SecureEmiCard.Application.Abstractions.Persistence;
using SecureEmiCard.Application.Abstractions.Security;
using SecureEmiCard.Application.Common.Exceptions;
using SecureEmiCard.Application.Common.Models;
using SecureEmiCard.Domain.Common;
using SecureEmiCard.Domain.Entities;

namespace SecureEmiCard.Application.Features.Cashback;

public interface ICashbackService
{
    CashbackRulesDto GetRules();
    Task<CashbackSummaryDto> GetCardSummaryAsync(int cardId, CancellationToken ct = default);
    Task<PagedResult<CashbackLogDto>> GetCardHistoryAsync(int cardId, int page, int pageSize, CancellationToken ct = default);
}

/// <summary>
/// Read side of the cashback module (rules, totals, history).
/// Cashback is WRITTEN by TransactionService, inside the swipe/refund database transaction.
/// </summary>
public class CashbackService : ICashbackService
{
    private readonly ICashbackEngine _engine;
    private readonly ICashbackRepository _cashback;
    private readonly ICreditCardRepository _cards;
    private readonly ICurrentUser _currentUser;

    public CashbackService(ICashbackEngine engine, ICashbackRepository cashback,
                           ICreditCardRepository cards, ICurrentUser currentUser)
    {
        _engine = engine;
        _cashback = cashback;
        _cards = cards;
        _currentUser = currentUser;
    }

    public CashbackRulesDto GetRules()
    {
        var rules = _engine.Rules;
        return new CashbackRulesDto(
            rules.CategoryPercentages
                 .OrderByDescending(r => r.Value).ThenBy(r => r.Key)
                 .Select(r => new CashbackRuleDto(r.Key, Describe(r.Key), r.Value))
                 .ToList(),
            rules.DefaultPercentage,
            rules.MinimumSpend,
            rules.MaxCashbackPerTransaction);
    }

    public async Task<CashbackSummaryDto> GetCardSummaryAsync(int cardId, CancellationToken ct = default)
    {
        var card = await GetAccessibleCardAsync(cardId, ct);
        var now = DateTime.UtcNow;
        var totals = await _cashback.GetTotalsAsync(cardId, new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc), ct);

        return new CashbackSummaryDto(
            card.CardId,
            card.MaskedCardNumber,
            totals.TotalEarned,
            totals.TotalReversed,
            totals.TotalEarned + totals.TotalReversed, // reversed amounts are negative
            totals.ThisMonthNet,
            totals.ByCategory
                  .OrderByDescending(c => c.NetAmount)
                  .Select(c => new CashbackCategoryTotalDto(c.MerchantCategoryCode, Describe(c.MerchantCategoryCode),
                                                            c.NetAmount, c.EarnedCount))
                  .ToList());
    }

    public async Task<PagedResult<CashbackLogDto>> GetCardHistoryAsync(int cardId, int page, int pageSize, CancellationToken ct = default)
    {
        await GetAccessibleCardAsync(cardId, ct);
        (page, pageSize) = PagedResult<CashbackLogDto>.Normalize(page, pageSize);
        var (items, total) = await _cashback.GetPagedByCardAsync(cardId, page, pageSize, ct);

        var dtos = items.Select(c => new CashbackLogDto(
            c.CashbackId, c.TransactionId, c.CardId,
            c.Transaction?.MerchantName ?? string.Empty,
            c.Transaction?.MerchantCategoryCode ?? string.Empty,
            c.Transaction?.Amount ?? 0m,
            c.CashbackPercentage, c.CashbackAmount, c.CashbackType.ToString(), c.CreditedDate)).ToList();

        return new PagedResult<CashbackLogDto>(dtos, page, pageSize, total);
    }

    private async Task<CreditCard> GetAccessibleCardAsync(int cardId, CancellationToken ct)
    {
        var card = await _cards.GetByIdAsync(cardId, ct);
        if (card is null || (!_currentUser.IsAdmin && card.CardholderId != _currentUser.UserId))
            throw new NotFoundException($"Card {cardId} was not found.");
        return card;
    }

    private static string Describe(string mcc) =>
        MerchantCategoryCodes.Descriptions.TryGetValue(mcc, out var d) ? d : $"MCC {mcc}";
}
