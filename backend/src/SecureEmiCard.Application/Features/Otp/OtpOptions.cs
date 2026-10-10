namespace SecureEmiCard.Application.Features.Otp;

/// <summary>
/// One-time code rules (Module 7). Optional "Otp" config section; the values below are the defaults.
/// </summary>
public class OtpOptions
{
    public const string SectionName = "Otp";
    public const int CodeLength = 6;

    /// <summary>How long a code is valid.</summary>
    public int ExpiryMinutes { get; set; } = 5;

    /// <summary>Wrong codes allowed per challenge. 3 tries out of 1,000,000 codes = 0.0003 % chance to guess.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>At most this many codes per cardholder per window - stops SMS flooding (and SMS cost abuse).</summary>
    public int MaxCodesPerWindow { get; set; } = 10;
    public int WindowMinutes { get; set; } = 15;

    /// <summary>Admins can do everything with all cards: their sign-in always needs a second factor.</summary>
    public bool RequireForAdminLogin { get; set; } = true;

    /// <summary>Also ask cardholders for a code at sign-in (off by default: their risky actions are protected).</summary>
    public bool RequireForCardholderLogin { get; set; }
}
