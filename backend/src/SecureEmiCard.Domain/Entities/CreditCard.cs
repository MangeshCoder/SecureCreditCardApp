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
    /// <summary>Consecutive wrong PINs after which the card is blocked automatically.</summary>
    public const int MaxFailedPinAttempts = 3;

    // Required by EF Core
    private CreditCard() { }

    public CreditCard(int cardholderId, string cardNumberEncrypted, string cardNumberHash, string maskedCardNumber,
                      string cvvHash, string pinHash, decimal creditLimit, DateOnly expiryDate)
    {
        if (creditLimit <= 0) throw new DomainException("Credit limit must be greater than zero.");
        if (expiryDate <= DateOnly.FromDateTime(DateTime.UtcNow))
            throw new DomainException("Expiry date must be in the future.");

        CardholderId = cardholderId;
        CardNumberEncrypted = cardNumberEncrypted;
        CardNumberHash = cardNumberHash;
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
    /// <summary>HMAC-SHA256 "blind index" of the card number, used to find a card from a swipe (Module 2).</summary>
    public string? CardNumberHash { get; private set; }
    public string MaskedCardNumber { get; private set; } = string.Empty;
    public string CvvHash { get; private set; } = string.Empty;
    public string PinHash { get; private set; } = string.Empty;
    public decimal CreditLimit { get; private set; }
    public decimal AvailableBalance { get; private set; }
    public CardStatus CardStatus { get; private set; }
    public DateOnly ExpiryDate { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public int FailedPinAttempts { get; private set; }

    public Cardholder? Cardholder { get; private set; }

    /// <summary>
    /// Amount currently owed on the card (dynamic balance calculation).
    /// Negative means a credit balance: the bank owes the customer (e.g. a refund after repayment).
    /// </summary>
    public decimal OutstandingAmount => CreditLimit - AvailableBalance;

    public bool IsExpired => ExpiryDate < DateOnly.FromDateTime(DateTime.UtcNow);

    public bool IsActive => CardStatus == CardStatus.Active;

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
        FailedPinAttempts = 0; // unblocking by the bank gives the customer fresh PIN attempts
    }

    /// <summary>
    /// Changes the credit limit while keeping the outstanding (spent) amount intact:
    /// newAvailable = newLimit - outstanding. The limit can never go below what is already spent.
    /// </summary>
    public void UpdateCreditLimit(decimal newLimit)
    {
        if (newLimit <= 0) throw new DomainException("Credit limit must be greater than zero.");
        var outstanding = OutstandingAmount; // may be negative (credit balance) - it is carried over
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

    /// <summary>Used once to back-fill cards issued before the blind index existed.</summary>
    public void SetCardNumberHash(string cardNumberHash)
    {
        if (CardNumberHash is not null) throw new DomainException("Card number hash is already set.");
        CardNumberHash = cardNumberHash;
    }

    // ---- Module 2: money movements ------------------------------------------------------

    /// <summary>
    /// Returns why a swipe of this amount must be declined, or null if it can be approved.
    /// Used by the authorization flow so every decline reason is decided in one place.
    /// </summary>
    public string? GetSwipeDeclineReason(decimal amount)
    {
        if (CardStatus == CardStatus.Blocked) return DeclineReasons.CardBlocked;
        if (IsExpired) return DeclineReasons.CardExpired;
        if (amount > AvailableBalance) return DeclineReasons.InsufficientCredit;
        return null;
    }

    /// <summary>Purchase: reduces the available balance.</summary>
    public void Debit(decimal amount)
    {
        if (amount <= 0) throw new DomainException("Amount must be greater than zero.");
        var reason = GetSwipeDeclineReason(amount);
        if (reason is not null) throw new DomainException(reason);
        AvailableBalance -= amount;
    }

    /// <summary>
    /// Repayment / balance load: increases the available balance. A customer cannot pay more than
    /// they owe. Allowed on blocked cards - a customer must always be able to repay.
    /// </summary>
    public void Credit(decimal amount)
    {
        if (amount <= 0) throw new DomainException("Amount must be greater than zero.");
        if (amount > OutstandingAmount)
            throw new DomainException($"Amount exceeds the outstanding amount ({Math.Max(0, OutstandingAmount):0.00}).");
        AvailableBalance += amount;
    }

    /// <summary>
    /// Merchant refund: always accepted, even if the purchase was already repaid. In that case the
    /// available balance goes above the credit limit, i.e. the card has a credit balance.
    /// </summary>
    public void CreditRefund(decimal amount)
    {
        if (amount <= 0) throw new DomainException("Amount must be greater than zero.");
        AvailableBalance += amount;
    }

    // ---- Module 3: cashback ------------------------------------------------------------

    /// <summary>
    /// Cashback is paid as a statement credit: it reduces what the customer owes.
    /// Like a refund it is always accepted (it may create a small credit balance).
    /// </summary>
    public void CreditReward(decimal amount)
    {
        if (amount <= 0) throw new DomainException("Cashback amount must be greater than zero.");
        AvailableBalance += amount;
    }

    /// <summary>Takes back cashback when the purchase that earned it is refunded.</summary>
    public void ReverseReward(decimal amount)
    {
        if (amount <= 0) throw new DomainException("Cashback amount must be greater than zero.");
        if (amount > AvailableBalance)
            throw new DomainException("Cashback cannot be reversed: not enough available balance.");
        AvailableBalance -= amount;
    }

    // ---- Module 2: PIN lockout ----------------------------------------------------------

    public int RemainingPinAttempts => Math.Max(0, MaxFailedPinAttempts - FailedPinAttempts);

    /// <summary>Records a wrong PIN; blocks the card after <see cref="MaxFailedPinAttempts"/> in a row.</summary>
    public void RegisterFailedPinAttempt()
    {
        FailedPinAttempts++;
        if (FailedPinAttempts >= MaxFailedPinAttempts && CardStatus == CardStatus.Active)
            CardStatus = CardStatus.Blocked;
    }

    public void ResetFailedPinAttempts() => FailedPinAttempts = 0;
}
