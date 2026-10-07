using SecureEmiCard.Domain.Common;
using SecureEmiCard.Domain.Enums;

namespace SecureEmiCard.Domain.Entities;

/// <summary>
/// One line of the card ledger. Maps to table dbo.Transactions.
/// (Named CardTransaction to avoid confusion with database transactions.)
/// Rows are never deleted or edited - a refund is a new Refund row plus a status change on the original.
/// </summary>
public class CardTransaction
{
    public const int MaxMerchantNameLength = 100;

    // Required by EF Core
    private CardTransaction() { }

    private CardTransaction(int cardId, string merchantName, string merchantCategoryCode, decimal amount,
                            TransactionType type, TransactionStatus status, string? declineReason)
    {
        if (amount <= 0) throw new DomainException("Transaction amount must be greater than zero.");
        if (string.IsNullOrWhiteSpace(merchantName)) throw new DomainException("Merchant name is required.");
        if (string.IsNullOrWhiteSpace(merchantCategoryCode)) throw new DomainException("Merchant category code is required.");

        CardId = cardId;
        MerchantName = merchantName.Trim();
        MerchantCategoryCode = merchantCategoryCode.Trim();
        Amount = amount;
        TransactionType = type;
        TransactionStatus = status;
        DeclineReason = declineReason;
        TransactionDate = DateTime.UtcNow;
    }

    public int TransactionId { get; private set; }
    public int CardId { get; private set; }
    public string MerchantName { get; private set; } = string.Empty;
    public string MerchantCategoryCode { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public TransactionType TransactionType { get; private set; }
    public TransactionStatus TransactionStatus { get; private set; }
    public bool IsEmiConverted { get; private set; }
    public DateTime TransactionDate { get; private set; }
    public string? DigitalSignature { get; private set; }
    public string? DeclineReason { get; private set; }

    public CreditCard? Card { get; private set; }

    public static CardTransaction ApprovedSwipe(int cardId, string merchant, string mcc, decimal amount) =>
        new(cardId, merchant, mcc, amount, TransactionType.Swipe, TransactionStatus.Completed, null);

    public static CardTransaction DeclinedSwipe(int cardId, string merchant, string mcc, decimal amount, string reason) =>
        new(cardId, merchant, mcc, amount, TransactionType.Swipe, TransactionStatus.Declined, reason);

    public static CardTransaction Load(int cardId, decimal amount) =>
        new(cardId, "Card repayment", MerchantCategoryCodes.FinancialInstitution, amount,
            TransactionType.Load, TransactionStatus.Completed, null);

    /// <summary>Ledger row for paying one EMI installment (principal + interest).</summary>
    public static CardTransaction EmiInstallment(int cardId, string description, decimal amount) =>
        new(cardId, description.Length > MaxMerchantNameLength ? description[..MaxMerchantNameLength] : description,
            MerchantCategoryCodes.FinancialInstitution, amount, TransactionType.EmiInstallment, TransactionStatus.Completed, null);

    /// <summary>Returns why this transaction cannot be converted to EMI, or null if it can.</summary>
    public string? GetEmiIneligibilityReason(decimal minimumAmount, int conversionWindowDays, DateTime nowUtc)
    {
        if (TransactionType != TransactionType.Swipe) return "Only purchases can be converted to EMI.";
        if (TransactionStatus == TransactionStatus.Refunded) return "A refunded purchase cannot be converted to EMI.";
        if (TransactionStatus != TransactionStatus.Completed) return "Only approved purchases can be converted to EMI.";
        if (IsEmiConverted) return "This purchase is already converted to EMI.";
        if (Amount <= minimumAmount) return $"Only purchases above {minimumAmount:0.00} can be converted to EMI.";
        if (TransactionDate < nowUtc.AddDays(-conversionWindowDays))
            return $"Purchases can be converted to EMI only within {conversionWindowDays} days.";
        return null;
    }

    public void MarkEmiConverted(decimal minimumAmount, int conversionWindowDays, DateTime nowUtc)
    {
        var reason = GetEmiIneligibilityReason(minimumAmount, conversionWindowDays, nowUtc);
        if (reason is not null) throw new DomainException(reason);
        IsEmiConverted = true;
    }

    /// <summary>Marks this swipe as refunded and returns the matching Refund ledger row.</summary>
    public CardTransaction Refund()
    {
        if (TransactionType != TransactionType.Swipe)
            throw new DomainException("Only swipe transactions can be refunded.");
        if (TransactionStatus != TransactionStatus.Completed)
            throw new DomainException($"A {TransactionStatus.ToString().ToLowerInvariant()} transaction cannot be refunded.");
        if (IsEmiConverted)
            throw new DomainException("A transaction converted to EMI cannot be refunded.");

        TransactionStatus = TransactionStatus.Refunded;
        return new CardTransaction(CardId, MerchantName, MerchantCategoryCode, Amount,
                                   TransactionType.Refund, TransactionStatus.Completed, null);
    }
}
