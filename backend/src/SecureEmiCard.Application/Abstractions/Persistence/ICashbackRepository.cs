using SecureEmiCard.Domain.Entities;

namespace SecureEmiCard.Application.Abstractions.Persistence;

/// <summary>Aggregated cashback figures for one card, computed by the database.</summary>
public record CashbackTotals(
    decimal TotalEarned,
    decimal TotalReversed,
    decimal ThisMonthNet,
    IReadOnlyList<(string MerchantCategoryCode, decimal NetAmount, int EarnedCount)> ByCategory);

public interface ICashbackRepository
{
    Task AddAsync(CashbackLog log, CancellationToken ct = default);

    /// <summary>All cashback rows (earned and reversed) belonging to the given swipes.</summary>
    Task<IReadOnlyList<CashbackLog>> GetByTransactionIdsAsync(IReadOnlyCollection<int> transactionIds, CancellationToken ct = default);

    /// <summary>Newest first, with the originating transaction loaded.</summary>
    Task<(IReadOnlyList<CashbackLog> Items, int TotalCount)> GetPagedByCardAsync(
        int cardId, int page, int pageSize, CancellationToken ct = default);

    Task<CashbackTotals> GetTotalsAsync(int cardId, DateTime monthStartUtc, CancellationToken ct = default);
}
