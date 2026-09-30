namespace SecureEmiCard.Domain.Common;

/// <summary>
/// Reasons returned to the merchant when a swipe is declined. They are deliberately coarse:
/// "Invalid card details" covers unknown number, wrong expiry and wrong CVV so a fraudster
/// cannot learn which part of a stolen card's data is wrong.
/// </summary>
public static class DeclineReasons
{
    public const string InvalidCardDetails = "Invalid card details";
    public const string IncorrectPin = "Incorrect PIN";
    public const string PinTriesExceeded = "PIN tries exceeded - card blocked";
    public const string CardBlocked = "Card blocked";
    public const string CardExpired = "Card expired";
    public const string InsufficientCredit = "Insufficient credit";
}
