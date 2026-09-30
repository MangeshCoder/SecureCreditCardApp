using SecureEmiCard.Domain.Entities;

namespace SecureEmiCard.Application.Abstractions.Persistence;

public interface ITransactionRepository
{
    Task<CardTransaction?> GetByIdAsync(int transactionId, CancellationToken ct = default);

    /// <summary>Newest first. cardId = null means all cards.</summary>
    Task<(IReadOnlyList<CardTransaction> Items, int TotalCount)> GetPagedAsync(
        int? cardId, int page, int pageSize, CancellationToken ct = default);

    Task AddAsync(CardTransaction transaction, CancellationToken ct = default);
}
