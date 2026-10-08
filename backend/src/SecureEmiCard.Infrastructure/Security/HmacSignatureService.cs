using SecureEmiCard.Application.Abstractions.Security;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace SecureEmiCard.Infrastructure.Security
{
    /// <summary>
    /// The specification's SignatureService (HMAC-SHA256), fixed:
    ///  - Verify compares with <see cref="CryptographicOperations.FixedTimeEquals"/>. The spec used
    ///    <c>computedSig == signature</c>: string comparison stops at the first different character, so by
    ///    measuring response times an attacker can discover a valid signature byte by byte (timing attack);
    ///  - keys are random bytes from Key Vault/configuration, at least 32 bytes, not strings.
    /// </summary>
    public class HmacSignatureService : ISignatureService
    {
        private const int MinKeySize = 32;

        public string Sign(string payload, byte[] key)
        {
            EnsureKey(key);
            return Convert.ToBase64String(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload)));
        }

        public bool Verify(string payload, string signature, byte[] key)
        {
            EnsureKey(key);
            byte[] expected = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload));

            Span<byte> provided = stackalloc byte[64];
            if (!Convert.TryFromBase64String(signature ?? string.Empty, provided, out int written) || written != expected.Length)
                return false;

            return CryptographicOperations.FixedTimeEquals(expected, provided[..written]);
        }

        private static void EnsureKey(byte[] key)
        {
            if (key is null || key.Length < MinKeySize)
                throw new CryptographicException("The signing key must be at least 32 bytes.");
        }
    }
}
