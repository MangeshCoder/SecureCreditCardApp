using SecureEmiCard.Domain.Entities;

namespace SecureEmiCard.Application.Abstractions.Persistence;

public interface ICardholderRepository
{
    Task<Cardholder?> GetByIdAsync(int cardholderId, CancellationToken ct = default);
    Task<Cardholder?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<bool> EmailExistsAsync(string email, CancellationToken ct = default);
    Task<IReadOnlyList<Cardholder>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(Cardholder cardholder, CancellationToken ct = default);
}
