using SecureEmiCard.Domain.Common;
using SecureEmiCard.Domain.Enums;

namespace SecureEmiCard.Domain.Entities;

/// <summary>
/// A credit card issued to a cardholder. Maps to table dbo.CreditCards.
/// The full card number is never stored in clear text: only an AES-256
/// cipher text and a masked version are persisted. CVV and PIN are stored
/// as salted, peppered PBKDF2 hashes.
/// </summary>
public class CreditCard
{
    // Required by EF Core
    private CreditCard() { }

    public CreditCard(int cardholderId, string cardNumberEncrypted, string maskedCardNumber,
                      string cvvHash, string pinHash, decimal creditLimit, DateOnly expiryDate)
    {
        if (creditLimit <= 0) throw new DomainException("Credit limit must be greater than zero.");
        if (expiryDate <= DateOnly.FromDateTime(DateTime.UtcNow))
            throw new DomainException("Expiry date must be in the future.");

        CardholderId = cardholderId;
        CardNumberEncrypted = cardNumberEncrypted;
        MaskedCardNumber = maskedCardNumber;
        CvvHash = cvvHash;
        PinHash = pinHash;
        CreditLimit = creditLimit;
        AvailableBalance = creditLimit; // a new card starts with nothing spent
        CardStatus = CardStatus.Active;
        ExpiryDate = expiryDate;
        CreatedAt = DateTime.UtcNow;
    }

    public int CardId { get; private set; }
    public int CardholderId { get; private set; }
    public string CardNumberEncrypted { get; private set; } = string.Empty;
    public string MaskedCardNumber { get; private set; } = string.Empty;
    public string CvvHash { get; private set; } = string.Empty;
    public string PinHash { get; private set; } = string.Empty;
    public decimal CreditLimit { get; private set; }
    public decimal AvailableBalance { get; private set; }
    public CardStatus CardStatus { get; private set; }
    public DateOnly ExpiryDate { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public Cardholder? Cardholder { get; private set; }

    /// <summary>Amount currently spent on the card (dynamic balance calculation).</summary>
    public decimal OutstandingAmount => CreditLimit - AvailableBalance;

    public bool IsExpired => ExpiryDate < DateOnly.FromDateTime(DateTime.UtcNow);

    public void Block()
    {
        if (CardStatus == CardStatus.Blocked) throw new DomainException("Card is already blocked.");
        CardStatus = CardStatus.Blocked;
    }

    public void Activate()
    {
        if (CardStatus == CardStatus.Active) throw new DomainException("Card is already active.");
        if (IsExpired) throw new DomainException("An expired card cannot be activated.");
        CardStatus = CardStatus.Active;
    }

    /// <summary>
    /// Changes the credit limit while keeping the outstanding (spent) amount intact:
    /// newAvailable = newLimit - outstanding. The limit can never go below what is already spent.
    /// </summary>
    public void UpdateCreditLimit(decimal newLimit)
    {
        if (newLimit <= 0) throw new DomainException("Credit limit must be greater than zero.");
        var outstanding = OutstandingAmount;
        if (newLimit < outstanding)
            throw new DomainException($"Credit limit cannot be lower than the outstanding amount ({outstanding:0.00}).");

        CreditLimit = newLimit;
        AvailableBalance = newLimit - outstanding;
    }

    public void ChangePin(string newPinHash)
    {
        if (CardStatus == CardStatus.Blocked) throw new DomainException("PIN cannot be changed on a blocked card.");
        if (string.IsNullOrWhiteSpace(newPinHash)) throw new DomainException("PIN hash is required.");
        PinHash = newPinHash;
    }
}
