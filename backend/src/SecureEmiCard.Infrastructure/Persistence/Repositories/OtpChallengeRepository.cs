using Microsoft.EntityFrameworkCore;
using SecureEmiCard.Application.Abstractions.Persistence;
using SecureEmiCard.Domain.Entities;
using SecureEmiCard.Domain.Enums;

namespace SecureEmiCard.Infrastructure.Persistence.Repositories;

public class OtpChallengeRepository : IOtpChallengeRepository
{
    private readonly AppDbContext _db;

    public OtpChallengeRepository(AppDbContext db) => _db = db;

    public Task<OtpChallenge?> GetByIdAsync(int otpChallengeId, CancellationToken ct = default) =>
        _db.OtpChallenges.FirstOrDefaultAsync(c => c.OtpChallengeId == otpChallengeId, ct);

    public Task<int> CountIssuedSinceAsync(int cardholderId, DateTime sinceUtc, CancellationToken ct = default) =>
        _db.OtpChallenges.CountAsync(c => c.CardholderId == cardholderId && c.CreatedAt >= sinceUtc, ct);

    public async Task<IReadOnlyList<OtpChallenge>> GetPendingAsync(int cardholderId, string contextHash, CancellationToken ct = default) =>
        await _db.OtpChallenges
                 .Where(c => c.CardholderId == cardholderId && c.ContextHash == contextHash
                             && c.Status == OtpChallengeStatus.Pending)
                 .ToListAsync(ct);

    public async Task AddAsync(OtpChallenge challenge, CancellationToken ct = default) =>
        await _db.OtpChallenges.AddAsync(challenge, ct);
}
