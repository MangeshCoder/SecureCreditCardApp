using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SecureEmiCard.Application.Abstractions.Security
{
    /// <summary>
    /// Symmetric AES-256 encryption of inter-bank payloads (spec §4A ICryptoService, improved):
    /// the key is a real 32-byte key (not a padded password), and a fresh random IV is used per message.
    /// Always combine with <see cref="ISignatureService"/> (encrypt-then-MAC) - encryption alone does
    /// not detect tampering.
    /// </summary>
    public interface IPayloadCryptoService
    {
        /// <returns>Base64( IV[16] | cipher text )</returns>
        string Encrypt(string plainText, byte[] key);

        string Decrypt(string cipherText, byte[] key);
    }
}
