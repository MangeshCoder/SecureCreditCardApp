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
    /// The specification's CryptoService (AES-256-CBC, random IV prepended to the cipher text), fixed:
    ///  - the key must be exactly 32 random bytes. The spec used <c>secretKey.PadRight(32).Substring(0, 32)</c>,
    ///    which turns a short password into a "key" that is mostly spaces;
    ///  - a new random IV for every message, so equal payloads never produce equal cipher texts;
    ///  - <c>Aes.EncryptCbc/DecryptCbc</c> (.NET 6+) instead of streams - less code, same result.
    ///
    /// CBC on its own does NOT detect tampering (and a server that reveals padding errors can even be used
    /// to decrypt messages - the "padding oracle" attack). That is why the gateway always verifies the
    /// HMAC signature over the cipher text FIRST and only decrypts authentic messages ("encrypt-then-MAC").
    /// </summary>
    public class AesCbcPayloadCryptoService : IPayloadCryptoService
    {
        private const int KeySize = 32; // AES-256
        private const int IvSize = 16;  // AES block size

        public string Encrypt(string plainText, byte[] key)
        {
            EnsureKey(key);
            using var aes = Aes.Create();
            aes.Key = key;

            byte[] iv = RandomNumberGenerator.GetBytes(IvSize);
            byte[] cipher = aes.EncryptCbc(Encoding.UTF8.GetBytes(plainText), iv, PaddingMode.PKCS7);

            byte[] output = new byte[IvSize + cipher.Length];
            iv.CopyTo(output, 0);                 // prepend IV (it is not secret, only unpredictable)
            cipher.CopyTo(output, IvSize);
            return Convert.ToBase64String(output);
        }

        public string Decrypt(string cipherText, byte[] key)
        {
            EnsureKey(key);
            byte[] input = Convert.FromBase64String(cipherText);
            if (input.Length < IvSize * 2 || (input.Length - IvSize) % IvSize != 0)
                throw new CryptographicException("Invalid cipher text length.");

            using var aes = Aes.Create();
            aes.Key = key;
            byte[] plain = aes.DecryptCbc(input.AsSpan(IvSize), input.AsSpan(0, IvSize), PaddingMode.PKCS7);
            return Encoding.UTF8.GetString(plain);
        }

        private static void EnsureKey(byte[] key)
        {
            if (key is null || key.Length != KeySize)
                throw new CryptographicException("The payload encryption key must be 32 bytes (AES-256).");
        }
    }
}
