namespace SecureEmiCard.Application.Abstractions.Security;

/// <summary>
/// Hashing for short card secrets (CVV, PIN). Because a PIN has only 10,000
/// possible values, a plain SHA-256 hash can be brute-forced instantly, so the
/// implementation mixes in a server-side secret "pepper" kept outside the database.
/// </summary>
public interface ISecretHasher
{
    string Hash(string secret);
    bool Verify(string secret, string storedHash);
}
