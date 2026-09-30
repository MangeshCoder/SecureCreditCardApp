namespace SecureEmiCard.Application.Abstractions.Security;

/// <summary>Salted, slow hashing for user passwords (PBKDF2).</summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string storedHash);
}
