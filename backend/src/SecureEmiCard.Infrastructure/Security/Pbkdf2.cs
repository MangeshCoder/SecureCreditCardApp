using System.Security.Cryptography;

namespace SecureEmiCard.Infrastructure.Security;

/// <summary>
/// Shared PBKDF2-HMAC-SHA256 helper. Output format (fits in NVARCHAR(256)):
///   "{prefix}${iterations}${base64 salt}${base64 hash}"
/// Storing the iteration count in the hash lets us raise it later without breaking old hashes.
/// </summary>
internal static class Pbkdf2
{
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public static string Hash(string prefix, byte[] secret, int iterations)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(secret, salt, iterations, HashAlgorithmName.SHA256, HashSize);
        return $"{prefix}${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string prefix, byte[] secret, string storedHash)
    {
        var parts = storedHash.Split('$');
        if (parts.Length != 4 || parts[0] != prefix || !int.TryParse(parts[1], out int iterations) || iterations <= 0)
            return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(secret, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        // Constant-time comparison prevents timing attacks (never use == on hashes/signatures).
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
