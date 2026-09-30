namespace SecureEmiCard.Application.Abstractions.Security;

/// <summary>
/// Deterministic keyed hash ("blind index") of a card number. Unlike the AES cipher text, the same
/// number always gives the same value, so a card can be found from the number a merchant sends
/// without storing or searching the number itself.
/// </summary>
public interface ICardLookupHasher
{
    string Compute(string cardNumber);
}
