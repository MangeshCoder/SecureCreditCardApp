using SecureEmiCard.Domain.Entities;

namespace SecureEmiCard.Application.Abstractions.Persistence;

public interface IOtpChallengeRepository
{
    Task<OtpChallenge?> GetByIdAsync(int otpChallengeId, CancellationToken ct = default);

    /// <summary>How many codes were sent to a cardholder since a moment (protects against SMS flooding).</summary>
    Task<int> CountIssuedSinceAsync(int cardholderId, DateTime sinceUtc, CancellationToken ct = default);

    /// <summary>Codes still waiting for the same action - superseded when a new one is sent.</summary>
    Task<IReadOnlyList<OtpChallenge>> GetPendingAsync(int cardholderId, string contextHash, CancellationToken ct = default);

    Task AddAsync(OtpChallenge challenge, CancellationToken ct = default);
}
