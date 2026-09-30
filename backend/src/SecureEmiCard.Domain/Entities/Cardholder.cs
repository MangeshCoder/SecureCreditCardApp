using SecureEmiCard.Domain.Common;
using SecureEmiCard.Domain.Enums;

namespace SecureEmiCard.Domain.Entities;

/// <summary>
/// A customer of the bank (or an administrator). Maps to table dbo.Cardholders.
/// </summary>
public class Cardholder
{
    private readonly List<CreditCard> _cards = new();

    // Required by EF Core
    private Cardholder() { }

    public Cardholder(string firstName, string lastName, string email, string phoneNumber,
                      string passwordHash, UserRole role = UserRole.Cardholder)
    {
        if (string.IsNullOrWhiteSpace(firstName)) throw new DomainException("First name is required.");
        if (string.IsNullOrWhiteSpace(lastName)) throw new DomainException("Last name is required.");
        if (string.IsNullOrWhiteSpace(email)) throw new DomainException("Email is required.");
        if (string.IsNullOrWhiteSpace(passwordHash)) throw new DomainException("Password hash is required.");

        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        Email = email.Trim().ToLowerInvariant();
        PhoneNumber = phoneNumber.Trim();
        PasswordHash = passwordHash;
        Role = role;
        CreatedAt = DateTime.UtcNow;
        IsActive = true;
    }

    public int CardholderId { get; private set; }
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string PhoneNumber { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public UserRole Role { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public bool IsActive { get; private set; }

    public IReadOnlyCollection<CreditCard> Cards => _cards.AsReadOnly();

    public string FullName => $"{FirstName} {LastName}";

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
