using SecureEmiCard.Domain.Enums;

namespace SecureEmiCard.Domain.Common;

/// <summary>
/// Where and how a card was used (Module 6): the channel, the merchant's country (ISO 3166 alpha-2)
/// and whether that country is abroad for the issuing bank. Stored on every swipe.
/// </summary>
public sealed record SwipeOrigin(TransactionChannel Channel, string? MerchantCountry, bool IsInternational)
{
    /// <summary>Chip + PIN at a shop at home - how every swipe worked before Module 6.</summary>
    public static readonly SwipeOrigin DomesticPos = new(TransactionChannel.Pos, null, false);
}
