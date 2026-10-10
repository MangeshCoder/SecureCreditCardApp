using SecureEmiCard.Domain.Enums;

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

    // Module 6: the cardholder's own controls. Clear wording on purpose: the customer sees these in the
    // statement and knows which switch or limit to change. (Card networks use ISO 8583 response codes
    // 57 "transaction not permitted to cardholder" and 61 "exceeds amount limit" for the same cases.)
    public const string CardLocked = "Card locked by cardholder";
    public const string ContactlessLimitExceeded = "Contactless limit exceeded - insert card and use PIN";
    public static readonly string InternationalDisabled = $"{TransactionChannels.InternationalName} disabled by cardholder";
    public static readonly string InternationalDailyLimitExceeded = $"Daily limit exceeded: {TransactionChannels.InternationalName}";

    public static string ChannelDisabled(TransactionChannel channel) =>
        $"{TransactionChannels.NameOf(channel)} disabled by cardholder";

    public static string DailyLimitExceeded(TransactionChannel channel) =>
        $"Daily limit exceeded: {TransactionChannels.NameOf(channel)}";
}
