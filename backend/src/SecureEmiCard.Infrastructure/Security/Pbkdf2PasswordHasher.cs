using System.Text;
using SecureEmiCard.Application.Abstractions.Security;

namespace SecureEmiCard.Infrastructure.Security;

/// <summary>
/// Password hashing with PBKDF2-HMAC-SHA256, 600,000 iterations and a random 16-byte salt per user
/// (OWASP recommendation). A fast hash such as plain SHA-256 must never be used for passwords.
/// </summary>
public class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const string Prefix = "PBKDF2-SHA256";
    public const int DefaultIterations = 600_000;

    private readonly int _iterations;

    public Pbkdf2PasswordHasher() : this(DefaultIterations) { }

    public Pbkdf2PasswordHasher(int iterations) => _iterations = iterations;

    public string Hash(string password) => Pbkdf2.Hash(Prefix, Encoding.UTF8.GetBytes(password), _iterations);

    public bool Verify(string password, string storedHash) =>
        Pbkdf2.Verify(Prefix, Encoding.UTF8.GetBytes(password), storedHash);
}
