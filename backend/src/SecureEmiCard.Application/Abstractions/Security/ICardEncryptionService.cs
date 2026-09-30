namespace SecureEmiCard.Application.Abstractions.Security;

/// <summary>AES-256 encryption for data that must be recoverable, e.g. the card number (PAN).</summary>
public interface ICardEncryptionService
{
    string Encrypt(string plainText);
    string Decrypt(string cipherText);
}
