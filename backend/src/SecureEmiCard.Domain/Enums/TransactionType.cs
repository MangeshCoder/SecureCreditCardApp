namespace SecureEmiCard.Domain.Enums;

/// <summary>Stored as NVARCHAR in Transactions.TransactionType.</summary>
public enum TransactionType
{
    /// <summary>Purchase at a merchant - reduces the available balance.</summary>
    Swipe,
    /// <summary>Repayment / balance load by the cardholder - increases the available balance.</summary>
    Load,
    /// <summary>Merchant refund of an earlier swipe - increases the available balance.</summary>
    Refund
}
