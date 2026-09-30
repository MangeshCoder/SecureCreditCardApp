namespace SecureEmiCard.Domain.Enums;

/// <summary>Stored as NVARCHAR in Transactions.TransactionStatus.</summary>
public enum TransactionStatus
{
    Completed,
    Declined,
    /// <summary>A completed swipe that the merchant later refunded.</summary>
    Refunded
}
