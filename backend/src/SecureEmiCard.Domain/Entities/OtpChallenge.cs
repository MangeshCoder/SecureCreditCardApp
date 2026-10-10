using SecureEmiCard.Domain.Common;
using SecureEmiCard.Domain.Enums;

namespace SecureEmiCard.Domain.Entities;

public enum OtpCheckResult
{
    Valid,
    Invalid,
    /// <summary>Wrong code, and no attempts are left - the challenge is now Failed.</summary>
    AttemptsExhausted,
    Expired,
    /// <summary>Already used, failed or superseded.</summary>
    NotPending
}

/// <summary>
/// One one-time code sent to a cardholder (Module 7). Maps to dbo.OtpChallenges.
/// The code itself is never stored - only a peppered hash, like the PIN.
/// A challenge is bound to ONE action through <see cref="ContextHash"/> (e.g. "unlock card 5", or
/// "pay 2,499.00 at Amazon"), so a code sent for one action cannot approve another.
/// </summary>
public class OtpChallenge
{
    // Required by EF Core
    private OtpChallenge() { }

    public static OtpChallenge Issue(int cardholderId, OtpPurpose purpose, string contextHash, string codeHash,
                                     string sentTo, DateTime nowUtc, TimeSpan lifetime, int maxAttempts)
    {
        if (string.IsNullOrWhiteSpace(contextHash)) throw new DomainException("Context hash is required.");
        if (string.IsNullOrWhiteSpace(codeHash)) throw new DomainException("Code hash is required.");
        if (lifetime <= TimeSpan.Zero) throw new DomainException("Lifetime must be positive.");
        if (maxAttempts < 1) throw new DomainException("At least one attempt must be allowed.");

        return new OtpChallenge
        {
            CardholderId = cardholderId,
            Purpose = purpose,
            ContextHash = contextHash,
            CodeHash = codeHash,
            SentTo = sentTo,
            Status = OtpChallengeStatus.Pending,
            MaxAttempts = maxAttempts,
            CreatedAt = nowUtc,
            ExpiresAt = nowUtc + lifetime
        };
    }

    public int OtpChallengeId { get; private set; }
    public int CardholderId { get; private set; }
    public OtpPurpose Purpose { get; private set; }
    public string ContextHash { get; private set; } = string.Empty;
    public string CodeHash { get; private set; } = string.Empty;
    /// <summary>Masked destination, e.g. +91******6072 (shown to the user, safe to store).</summary>
    public string SentTo { get; private set; } = string.Empty;
    public OtpChallengeStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public int MaxAttempts { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime? UsedAt { get; private set; }

    public int RemainingAttempts => Math.Max(0, MaxAttempts - Attempts);

    /// <summary>
    /// Checks a code. <paramref name="codeMatches"/> does the (constant-time) hash comparison, so the
    /// domain doesn't depend on the crypto implementation. A correct code is accepted exactly once.
    /// </summary>
    public OtpCheckResult Verify(Func<string, bool> codeMatches, DateTime nowUtc)
    {
        if (Status != OtpChallengeStatus.Pending) return OtpCheckResult.NotPending;
        if (nowUtc >= ExpiresAt) return OtpCheckResult.Expired;

        if (codeMatches(CodeHash))
        {
            Status = OtpChallengeStatus.Used;
            UsedAt = nowUtc;
            return OtpCheckResult.Valid;
        }

        Attempts++;
        if (Attempts < MaxAttempts) return OtpCheckResult.Invalid;
        Status = OtpChallengeStatus.Failed;
        return OtpCheckResult.AttemptsExhausted;
    }

    /// <summary>A newer code was sent for the same action; this one stops working.</summary>
    public void Supersede()
    {
        if (Status == OtpChallengeStatus.Pending) Status = OtpChallengeStatus.Superseded;
    }
}
