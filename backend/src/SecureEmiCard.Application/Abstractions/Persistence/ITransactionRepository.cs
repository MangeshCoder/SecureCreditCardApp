using SecureEmiCard.Domain.Common;
using SecureEmiCard.Domain.Entities;

namespace SecureEmiCard.Application.Abstractions.Persistence;

public interface ITransactionRepository
{
    Task<CardTransaction?> GetByIdAsync(int transactionId, CancellationToken ct = default);

    /// <summary>Newest first. cardId = null means all cards.</summary>
    Task<(IReadOnlyList<CardTransaction> Items, int TotalCount)> GetPagedAsync(
        int? cardId, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Approved swipes of a card since a date that are not yet converted to EMI (newest first).</summary>
    Task<IReadOnlyList<CardTransaction>> GetConvertibleSwipesAsync(int cardId, DateTime sinceUtc, CancellationToken ct = default);

    /// <summary>
    /// Module 6: approved swipes of a card since a moment, summed per channel and abroad.
    /// A purchase that was later refunded still counts - refunds don't give back daily limit.
    /// </summary>
    Task<DailySpend> GetApprovedSpendAsync(int cardId, DateTime sinceUtc, CancellationToken ct = default);

    /// <summary>
    /// Module 8: rows not on any statement yet that change what is owed - approved swipes, repayments, refunds
    /// and charges (tracked, so they can be marked billed). Declined swipes and EMI installments are not billed.
    /// </summary>
    Task<IReadOnlyList<CardTransaction>> GetUnbilledAsync(int cardId, CancellationToken ct = default);

    /// <summary>Module 8: the rows billed on a statement, oldest first.</summary>
    Task<IReadOnlyList<CardTransaction>> GetByStatementAsync(int statementId, CancellationToken ct = default);

    /// <summary>Module 8: repayments (Load) made in (after, until] - what counts as "paid by the due date".</summary>
    Task<decimal> SumPaymentsAsync(int cardId, DateTime afterUtc, DateTime untilUtc, CancellationToken ct = default);

    Task AddAsync(CardTransaction transaction, CancellationToken ct = default);
}
