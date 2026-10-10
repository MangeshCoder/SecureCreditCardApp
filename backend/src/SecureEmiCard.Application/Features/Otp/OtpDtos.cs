namespace SecureEmiCard.Application.Features.Otp;

/// <summary>Returned with HTTP 428 so the client can ask for the code. Never contains the code itself.</summary>
public record OtpChallengeDto(
    int ChallengeId,
    string Purpose,
    string Description,
    string SentTo,
    DateTime ExpiresAt,
    int CodeLength);
