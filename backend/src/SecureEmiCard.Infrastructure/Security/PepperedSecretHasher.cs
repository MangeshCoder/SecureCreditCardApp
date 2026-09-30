using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SecureEmiCard.Application.Abstractions.Security;

namespace SecureEmiCard.Infrastructure.Security;

/// <summary>
/// Hashes CVV and PIN values: HMAC-SHA256(pepper, secret) followed by salted PBKDF2.
///
/// The specification suggests plain SHA-256, but a 4-digit PIN has only 10,000 combinations
/// and a CVV only 1,000 - anyone with a database dump could reverse a SHA-256 hash in milliseconds.
/// The pepper is a secret key stored outside the database (Key Vault), so a stolen database
/// alone is not enough to brute-force the values.
/// </summary>
public class PepperedSecretHasher : ISecretHasher
{
    private const string Prefix = "HPBKDF2-SHA256";
    public const int DefaultIterations = 100_000;

    private readonly byte[] _pepper;
    private readonly int _iterations;

    public PepperedSecretHasher(IOptions<EncryptionOptions> options) : this(options, DefaultIterations) { }

    public PepperedSecretHasher(IOptions<EncryptionOptions> options, int iterations)
    {
        _pepper = Convert.FromBase64String(options.Value.SecretPepper);
        if (_pepper.Length < 32)
            throw new InvalidOperationException("Encryption:SecretPepper must be a Base64 encoded key of at least 32 bytes.");
        _iterations = iterations;
    }

    public string Hash(string secret) => Pbkdf2.Hash(Prefix, Pepper(secret), _iterations);

    public bool Verify(string secret, string storedHash) => Pbkdf2.Verify(Prefix, Pepper(secret), storedHash);

    private byte[] Pepper(string secret) => HMACSHA256.HashData(_pepper, Encoding.UTF8.GetBytes(secret));
}
