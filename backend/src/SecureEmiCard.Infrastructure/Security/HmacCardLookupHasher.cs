using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SecureEmiCard.Application.Abstractions.Security;

namespace SecureEmiCard.Infrastructure.Security;

/// <summary>
/// Blind index for card numbers: HMAC-SHA256(lookupKey, cardNumber) as 64 hex characters.
///
/// The lookup key is DERIVED from Encryption:SecretPepper with HKDF using a unique "info" label.
/// This is key separation: one master secret in Key Vault, but the PIN pepper and the lookup key
/// are cryptographically independent, so no new configuration value is needed.
///
/// A keyed HMAC (not plain SHA-256) is essential: there are only ~10^9 possible numbers for our BIN,
/// so an unkeyed hash could be brute-forced back to the card number.
/// </summary>
public class HmacCardLookupHasher : ICardLookupHasher
{
    private static readonly byte[] Info = Encoding.UTF8.GetBytes("SecureEmiCard.CardNumberLookup.v1");

    private readonly byte[] _key;

    public HmacCardLookupHasher(IOptions<EncryptionOptions> options)
    {
        var masterKey = Convert.FromBase64String(options.Value.SecretPepper);
        _key = HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, outputLength: 32, info: Info);
    }

    public string Compute(string cardNumber) =>
        Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(cardNumber)));
}
