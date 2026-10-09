namespace SecureEmiCard.Api.InterBank
{
    /// <summary>Bound from the "InterBank" configuration section.</summary>
    public class InterBankOptions
    {
        public const string SectionName = "InterBank";

        /// <summary>How far X-Timestamp may be from our clock (seconds). Limits the replay window.</summary>
        public int AllowedClockSkewSeconds { get; set; } = 300;

        /// <summary>How long a used nonce is remembered. Must be longer than 2 × the clock skew.</summary>
        public int NonceTtlSeconds { get; set; } = 900;

        /// <summary>Largest accepted request body - stops memory exhaustion by huge payloads.</summary>
        public int MaxBodyBytes { get; set; } = 64 * 1024;

        public List<PartnerBankOptions> Partners { get; set; } = new();
    }

    /// <summary>
    /// One partner bank (acquirer / payment gateway). In production the keys live in Azure Key Vault,
    /// one secret per partner, and are exchanged with the partner through a secure out-of-band process.
    /// </summary>
    public class PartnerBankOptions
    {
        public string PartnerId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        /// <summary>Base64, exactly 32 bytes - AES-256 key for the payload.</summary>
        public string EncryptionKey { get; set; } = string.Empty;
        /// <summary>Base64, at least 32 bytes - HMAC-SHA256 key for signatures. Must differ from the encryption key.</summary>
        public string SigningKey { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
    }
}
