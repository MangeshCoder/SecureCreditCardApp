using SecureEmiCard.Application.Features.Otp;

namespace SecureEmiCard.Application.Common.Exceptions;

/// <summary>
/// Mapped to HTTP 428 Precondition Required: "send this request again with the code we just sent".
/// The response carries the challenge (id, masked phone number, expiry) - never the code.
/// </summary>
public class OtpRequiredException : Exception
{
    public OtpRequiredException(OtpChallengeDto challenge)
        : base($"Enter the {challenge.CodeLength}-digit code sent to {challenge.SentTo}.") => Challenge = challenge;

    public OtpChallengeDto Challenge { get; }
}
