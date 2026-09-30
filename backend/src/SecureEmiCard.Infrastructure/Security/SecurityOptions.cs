namespace SecureEmiCard.Infrastructure.Security;

/// <summary>
/// Bound from the "Encryption" configuration section.
/// In production these values come from Azure Key Vault / environment variables, never from source control.
/// </summary>
public class EncryptionOptions
{
    public const string SectionName = "Encryption";

    /// <summary>Base64 encoded 32-byte (256-bit) AES key used to encrypt card numbers at rest.</summary>
    public string CardDataKey { get; set; } = string.Empty;

    /// <summary>Base64 encoded secret (at least 32 bytes) mixed into CVV/PIN hashes ("pepper").</summary>
    public string SecretPepper { get; set; } = string.Empty;
}

/// <summary>Bound from the "Jwt" configuration section.</summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;

    /// <summary>Base64 encoded key of at least 64 bytes (512 bits) for HMAC-SHA512 signing.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public int ExpiryMinutes { get; set; } = 60;
}
