using SecureEmiCard.Domain.Enums;

namespace SecureEmiCard.Domain.Common;

/// <summary>Display names of the channels, used in decline reasons and messages.</summary>
public static class TransactionChannels
{
    public const string InternationalName = "International use";

    public static string NameOf(TransactionChannel channel) => channel switch
    {
        TransactionChannel.Pos => "Shop (POS) payments",
        TransactionChannel.Online => "Online payments",
        TransactionChannel.Contactless => "Contactless payments",
        TransactionChannel.Atm => "ATM withdrawals",
        _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, null)
    };
}
