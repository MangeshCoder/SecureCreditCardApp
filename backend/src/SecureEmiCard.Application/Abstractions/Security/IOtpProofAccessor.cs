namespace SecureEmiCard.Application.Abstractions.Security;

/// <summary>The one-time code the client sent with this request (Module 7).</summary>
public sealed record OtpProof(int ChallengeId, string Code);

/// <summary>
/// Where the code comes from. The API reads the headers X-Otp-Challenge-Id and X-Otp-Code, so the
/// protected requests themselves (DTOs, routes) don't change when an OTP is added to them.
/// </summary>
public interface IOtpProofAccessor
{
    OtpProof? Current { get; }
}
