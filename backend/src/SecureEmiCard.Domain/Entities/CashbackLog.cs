using SecureEmiCard.Domain.Common;
using SecureEmiCard.Domain.Enums;

namespace SecureEmiCard.Domain.Entities;

/// <summary>
/// One line of the cashback ledger. Maps to table dbo.CashbackLogs.
/// Rows are never edited: a refund adds a "Reversed" row with the negative amount,
/// so SUM(CashbackAmount) is always the net cashback.
/// </summary>
public class CashbackLog
{
    // Required by EF Core
    private CashbackLog() { }

    private CashbackLog(CardTransaction? transaction, int transactionId, int cardId,
                        decimal percentage, decimal amount, CashbackType type)
    {
        Transaction = transaction;
        TransactionId = transactionId;
        CardId = cardId;
        CashbackPercentage = percentage;
        CashbackAmount = amount;
        CashbackType = type;
        CreditedDate = DateTime.UtcNow;
    }

    public int CashbackId { get; private set; }
    public int TransactionId { get; private set; }
    public int CardId { get; private set; }
    /// <summary>Percentage applied, e.g. 3.00 for 3 %.</summary>
    public decimal CashbackPercentage { get; private set; }
    /// <summary>Positive for Earned, negative for Reversed.</summary>
    public decimal CashbackAmount { get; private set; }
    public CashbackType CashbackType { get; private set; }
    public DateTime CreditedDate { get; private set; }

    public CardTransaction? Transaction { get; private set; }

    /// <summary>Module 8: the statement that billed this row (null = unbilled, it goes on the next statement).</summary>
    public int? StatementId { get; private set; }
    public CardStatement? Statement { get; private set; }

    /// <summary>Puts this row on a statement. Uses the navigation, so EF fills in the new statement's id.</summary>
    public void MarkBilled(CardStatement statement)
    {
        if (StatementId is not null || Statement is not null) throw new DomainException("Already on a statement.");
        Statement = statement;
    }

    /// <summary>
    /// Cashback for an approved swipe. Takes the transaction object (not its id) because the swipe
    /// and its cashback are saved together - the id is only known after the INSERT, and EF Core
    /// fills in the foreign key from the navigation property.
    /// </summary>
    public static CashbackLog Earned(CardTransaction swipe, decimal percentage, decimal amount)
    {
        if (swipe.TransactionType != TransactionType.Swipe || swipe.TransactionStatus != TransactionStatus.Completed)
            throw new DomainException("Cashback can only be earned on an approved swipe.");
        if (amount <= 0) throw new DomainException("Cashback amount must be greater than zero.");
        if (percentage is <= 0 or > 100) throw new DomainException("Cashback percentage must be between 0 and 100.");

        return new CashbackLog(swipe, swipe.TransactionId, swipe.CardId, percentage, amount, CashbackType.Earned);
    }

    /// <summary>The matching negative entry when the purchase is refunded.</summary>
    public CashbackLog CreateReversal()
    {
        if (CashbackType != CashbackType.Earned) throw new DomainException("Only earned cashback can be reversed.");
        return new CashbackLog(null, TransactionId, CardId, CashbackPercentage, -CashbackAmount, CashbackType.Reversed);
    }
}
