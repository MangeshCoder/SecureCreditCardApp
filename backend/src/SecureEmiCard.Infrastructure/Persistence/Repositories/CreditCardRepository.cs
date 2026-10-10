using Microsoft.EntityFrameworkCore;
using SecureEmiCard.Application.Abstractions.Persistence;
using SecureEmiCard.Domain.Entities;

namespace SecureEmiCard.Infrastructure.Persistence.Repositories;

public class CreditCardRepository : ICreditCardRepository
{
    private readonly AppDbContext _db;

    public CreditCardRepository(AppDbContext db) => _db = db;

    // Single-card reads include the controls (Module 6): every swipe needs them.
    public Task<CreditCard?> GetByIdAsync(int cardId, CancellationToken ct = default) =>
        _db.CreditCards.Include(c => c.Controls).FirstOrDefaultAsync(c => c.CardId == cardId, ct);

    public Task<CreditCard?> GetByNumberHashAsync(string cardNumberHash, CancellationToken ct = default) =>
        _db.CreditCards.Include(c => c.Controls).FirstOrDefaultAsync(c => c.CardNumberHash == cardNumberHash, ct);

    public Task<bool> NumberHashExistsAsync(string cardNumberHash, CancellationToken ct = default) =>
        _db.CreditCards.AnyAsync(c => c.CardNumberHash == cardNumberHash, ct);

    public async Task<IReadOnlyList<CreditCard>> GetByCardholderAsync(int cardholderId, CancellationToken ct = default) =>
        await _db.CreditCards.AsNoTracking().Where(c => c.CardholderId == cardholderId)
                 .OrderBy(c => c.CardId).ToListAsync(ct);

    public async Task<IReadOnlyList<CreditCard>> GetAllAsync(CancellationToken ct = default) =>
        await _db.CreditCards.AsNoTracking().OrderBy(c => c.CardId).ToListAsync(ct);

    public async Task AddAsync(CreditCard card, CancellationToken ct = default) =>
        await _db.CreditCards.AddAsync(card, ct);
}
