namespace SecureEmiCard.Api;

public static class RateLimitPolicies
{
    /// <summary>
    /// Applied to login/register and PIN endpoints: a small number of attempts per minute per
    /// client IP, which makes brute-forcing passwords or 4-digit PINs impractical.
    /// </summary>
    public const string Sensitive = "sensitive";
}
