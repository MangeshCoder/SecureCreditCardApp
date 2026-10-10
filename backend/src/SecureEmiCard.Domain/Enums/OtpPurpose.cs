namespace SecureEmiCard.Domain.Enums;

/// <summary>What a one-time code approves (Module 7). Stored as NVARCHAR in OtpChallenges.Purpose.</summary>
public enum OtpPurpose
{
    /// <summary>Second step of signing in (always for admins).</summary>
    Login,
    /// <summary>Online (card-not-present) purchase: RBI "additional factor of authentication", 3-D Secure.</summary>
    OnlinePayment,
    UnlockCard,
    /// <summary>A card-control change that makes the card riskier (switching something on, raising a limit).</summary>
    CardControls,
    ChangePin,
    RevealCardNumber
}
