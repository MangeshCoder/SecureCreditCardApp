using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SecureEmiCard.Application.Abstractions.Security
{
    /// <summary>HMAC-SHA256 digital signatures (spec §4A ISignatureService, improved: constant-time verify).</summary>
    public interface ISignatureService
    {
        /// <returns>Base64 HMAC-SHA256 of the UTF-8 payload.</returns>
        string Sign(string payload, byte[] key);

        /// <summary>Recomputes the signature and compares in constant time.</summary>
        bool Verify(string payload, string signature, byte[] key);
    }
}
