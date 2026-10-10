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
                            TransactionType type, TransactionStatus status, string? declineReason,
                            SwipeOrigin? origin = null)
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
        Channel = origin?.Channel;
        MerchantCountry = origin?.MerchantCountry;
        IsInternational = origin?.IsInternational ?? false;
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
    /// <summary>Module 6: how the card was used. Null for repayments and EMI installments.</summary>
    public TransactionChannel? Channel { get; private set; }
    /// <summary>Module 6: ISO 3166 alpha-2 country of the merchant (null for rows from before Module 6).</summary>
    public string? MerchantCountry { get; private set; }
    public bool IsInternational { get; private set; }

    public CreditCard? Card { get; private set; }

    /// <summary>Module 8: the statement that billed this row (null = unbilled, it goes on the next statement).</summary>
    public int? StatementId { get; private set; }
    public CardStatement? Statement { get; private set; }

    /// <summary>Puts this row on a statement. Uses the navigation, so EF fills in the new statement's id.</summary>
    public void MarkBilled(CardStatement statement)
    {
        if (StatementId is not null || Statement is not null) throw new DomainException("Already on a statement.");
        Statement = statement;
    }

    public bool IsCashWithdrawal => Channel == TransactionChannel.Atm;

    public static CardTransaction ApprovedSwipe(int cardId, string merchant, string mcc, decimal amount,
                                                SwipeOrigin? origin = null) =>
        new(cardId, merchant, mcc, amount, TransactionType.Swipe, TransactionStatus.Completed, null,
            origin ?? SwipeOrigin.DomesticPos);

    public static CardTransaction DeclinedSwipe(int cardId, string merchant, string mcc, decimal amount, string reason,
                                                SwipeOrigin? origin = null) =>
        new(cardId, merchant, mcc, amount, TransactionType.Swipe, TransactionStatus.Declined, reason,
            origin ?? SwipeOrigin.DomesticPos);

    public static CardTransaction Load(int cardId, decimal amount) =>
        new(cardId, "Card repayment", MerchantCategoryCodes.FinancialInstitution, amount,
            TransactionType.Load, TransactionStatus.Completed, null);

    /// <summary>
    /// Stores the partner bank's HMAC signature of the request that created this transaction
    /// (spec: Transactions.DigitalSignature). Proof that the partner really sent it - non-repudiation.
    /// </summary>
    public void AttachDigitalSignature(string signature)
    {
        if (string.IsNullOrWhiteSpace(signature)) throw new DomainException("Signature is required.");
        if (DigitalSignature is not null) throw new DomainException("A digital signature is already attached.");
        DigitalSignature = signature.Length > 512 ? signature[..512] : signature;
    }

    public bool IsCharge => TransactionType is TransactionType.Fee or TransactionType.Interest or TransactionType.Tax;

    /// <summary>
    /// Module 8: a bank charge (fee, interest or GST) on the ledger. <paramref name="atUtc"/> is given by the
    /// billing run, so a charge belongs exactly to the billing period it was computed for.
    /// </summary>
    public static CardTransaction Charge(int cardId, TransactionType type, string description, decimal amount, DateTime atUtc)
    {
        if (type is not (TransactionType.Fee or TransactionType.Interest or TransactionType.Tax))
            throw new DomainException("Only fees, interest and taxes are charges.");
        var charge = new CardTransaction(cardId,
            description.Length > MaxMerchantNameLength ? description[..MaxMerchantNameLength] : description,
            MerchantCategoryCodes.FinancialInstitution, amount, type, TransactionStatus.Completed, null);
        charge.TransactionDate = atUtc;
        return charge;
    }

    /// <summary>Ledger row for paying one EMI installment (principal + interest).</summary>
    public static CardTransaction EmiInstallment(int cardId, string description, decimal amount) =>
        new(cardId, description.Length > MaxMerchantNameLength ? description[..MaxMerchantNameLength] : description,
            MerchantCategoryCodes.FinancialInstitution, amount, TransactionType.EmiInstallment, TransactionStatus.Completed, null);

    /// <summary>Returns why this transaction cannot be converted to EMI, or null if it can.</summary>
    public string? GetEmiIneligibilityReason(decimal minimumAmount, int conversionWindowDays, DateTime nowUtc)
    {
        if (TransactionType != TransactionType.Swipe) return "Only purchases can be converted to EMI.";
        if (IsCashWithdrawal) return "Cash withdrawals cannot be converted to EMI.";
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
        if (IsCashWithdrawal)
            throw new DomainException("A cash withdrawal cannot be refunded.");
        if (TransactionStatus != TransactionStatus.Completed)
            throw new DomainException($"A {TransactionStatus.ToString().ToLowerInvariant()} transaction cannot be refunded.");
        if (IsEmiConverted)
            throw new DomainException("A transaction converted to EMI cannot be refunded.");

        TransactionStatus = TransactionStatus.Refunded;
        return new CardTransaction(CardId, MerchantName, MerchantCategoryCode, Amount,
                                   TransactionType.Refund, TransactionStatus.Completed, null,
                                   Channel is null ? null : new SwipeOrigin(Channel.Value, MerchantCountry, IsInternational));
    }
}
