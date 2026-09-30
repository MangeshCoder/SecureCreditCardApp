using SecureEmiCard.Domain.Entities;

namespace SecureEmiCard.Application.Abstractions.Persistence;

public interface ICreditCardRepository
{
    Task<CreditCard?> GetByIdAsync(int cardId, CancellationToken ct = default);
    Task<IReadOnlyList<CreditCard>> GetByCardholderAsync(int cardholderId, CancellationToken ct = default);
    Task<IReadOnlyList<CreditCard>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(CreditCard card, CancellationToken ct = default);
}
