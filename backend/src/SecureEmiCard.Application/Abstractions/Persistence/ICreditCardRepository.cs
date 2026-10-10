using SecureEmiCard.Domain.Entities;

namespace SecureEmiCard.Application.Abstractions.Persistence;

public interface ICreditCardRepository
{
    Task<CreditCard?> GetByIdAsync(int cardId, CancellationToken ct = default);
    Task<CreditCard?> GetByNumberHashAsync(string cardNumberHash, CancellationToken ct = default);
    Task<bool> NumberHashExistsAsync(string cardNumberHash, CancellationToken ct = default);
    Task<IReadOnlyList<CreditCard>> GetByCardholderAsync(int cardholderId, CancellationToken ct = default);
    Task<IReadOnlyList<CreditCard>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(CreditCard card, CancellationToken ct = default);

    /// <summary>Module 8: cards whose billing cycle started on or before a moment (last statement, or issuance).</summary>
    Task<IReadOnlyList<int>> GetIdsWithCycleStartedOnOrBeforeAsync(DateTime cycleStartUtc, CancellationToken ct = default);
}
