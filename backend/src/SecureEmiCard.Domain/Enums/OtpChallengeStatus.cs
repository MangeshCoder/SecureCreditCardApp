namespace SecureEmiCard.Domain.Enums;

/// <summary>Stored as NVARCHAR in OtpChallenges.Status.</summary>
public enum OtpChallengeStatus
{
    /// <summary>Sent, waiting for the code.</summary>
    Pending,
    /// <summary>The correct code was entered - the challenge can never be used again.</summary>
    Used,
    /// <summary>Too many wrong codes.</summary>
    Failed,
    /// <summary>Replaced by a newer code for the same action ("Send a new code").</summary>
    Superseded
}
