namespace SecureEmiCard.Domain.Enums;

/// <summary>
/// How the card was used (Module 6). Stored as NVARCHAR in Transactions.Channel.
/// Each channel can be switched on/off and given a daily limit by the cardholder (CardControls).
/// </summary>
public enum TransactionChannel
{
    /// <summary>Card inserted at a shop terminal, chip + PIN ("point of sale").</summary>
    Pos,
    /// <summary>Card-not-present: e-commerce websites and apps.</summary>
    Online,
    /// <summary>Tap to pay (NFC). Limited per transaction by the regulator.</summary>
    Contactless,
    /// <summary>Cash withdrawal at an ATM (merchant category 6011).</summary>
    Atm
}
