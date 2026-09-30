using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SecureEmiCard.Application.Abstractions.Security;

namespace SecureEmiCard.Infrastructure.Security;

/// <summary>
/// AES-256-GCM encryption for card numbers stored in CreditCards.CardNumberEncrypted.
///
/// Why GCM instead of the CBC mode shown in the specification?
///  - GCM is "authenticated encryption": it adds a 16-byte tag, so any tampering with the
///    stored cipher text is detected on decryption (CBC alone cannot detect it).
///  - A fresh random 12-byte nonce is generated for every encryption, so the same card number
///    never produces the same cipher text twice.
///
/// Stored format:  "v1:" + Base64( nonce[12] | tag[16] | cipherText[n] )
/// The "v1" prefix allows key rotation later (v2 = new key) without breaking existing rows.
/// </summary>
public class AesGcmCardEncryptionService : ICardEncryptionService
{
    private const string Version = "v1:";
    private const int NonceSize = 12; // AesGcm.NonceByteSizes.MaxSize
    private const int TagSize = 16;   // AesGcm.TagByteSizes.MaxSize

    private readonly byte[] _key;

    public AesGcmCardEncryptionService(IOptions<EncryptionOptions> options)
    {
        _key = Convert.FromBase64String(options.Value.CardDataKey);
        if (_key.Length != 32)
            throw new InvalidOperationException("Encryption:CardDataKey must be a Base64 encoded 32-byte key (AES-256).");
    }

    public string Encrypt(string plainText)
    {
        byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
        byte[] output = new byte[NonceSize + TagSize + plainBytes.Length];

        Span<byte> nonce = output.AsSpan(0, NonceSize);
        Span<byte> tag = output.AsSpan(NonceSize, TagSize);
        Span<byte> cipher = output.AsSpan(NonceSize + TagSize);

        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        return Version + Convert.ToBase64String(output);
    }

    public string Decrypt(string cipherText)
    {
        if (!cipherText.StartsWith(Version, StringComparison.Ordinal))
            throw new CryptographicException("Unsupported cipher text version.");

        byte[] input = Convert.FromBase64String(cipherText[Version.Length..]);
        if (input.Length < NonceSize + TagSize)
            throw new CryptographicException("Cipher text is too short.");

        ReadOnlySpan<byte> nonce = input.AsSpan(0, NonceSize);
        ReadOnlySpan<byte> tag = input.AsSpan(NonceSize, TagSize);
        ReadOnlySpan<byte> cipher = input.AsSpan(NonceSize + TagSize);

        byte[] plainBytes = new byte[cipher.Length];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plainBytes); // throws AuthenticationTagMismatchException if tampered

        return Encoding.UTF8.GetString(plainBytes);
    }
}
