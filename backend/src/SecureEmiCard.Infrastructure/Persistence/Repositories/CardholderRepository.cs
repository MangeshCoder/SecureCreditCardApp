using Microsoft.EntityFrameworkCore;
using SecureEmiCard.Application.Abstractions.Persistence;
using SecureEmiCard.Domain.Entities;

namespace SecureEmiCard.Infrastructure.Persistence.Repositories;

public class CardholderRepository : ICardholderRepository
{
    private readonly AppDbContext _db;

    public CardholderRepository(AppDbContext db) => _db = db;

    public Task<Cardholder?> GetByIdAsync(int cardholderId, CancellationToken ct = default) =>
        _db.Cardholders.Include(c => c.Cards).FirstOrDefaultAsync(c => c.CardholderId == cardholderId, ct);

    public Task<Cardholder?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        _db.Cardholders.FirstOrDefaultAsync(c => c.Email == Normalize(email), ct);

    public Task<bool> EmailExistsAsync(string email, CancellationToken ct = default) =>
        _db.Cardholders.AnyAsync(c => c.Email == Normalize(email), ct);

    public async Task<IReadOnlyList<Cardholder>> GetAllAsync(CancellationToken ct = default) =>
        await _db.Cardholders.AsNoTracking().Include(c => c.Cards)
                 .OrderBy(c => c.CardholderId).ToListAsync(ct);

    public async Task AddAsync(Cardholder cardholder, CancellationToken ct = default) =>
        await _db.Cardholders.AddAsync(cardholder, ct);

    // Emails are stored lower-cased by the Cardholder entity.
    private static string Normalize(string email) => email.Trim().ToLowerInvariant();
}
