namespace SecureEmiCard.Domain.Enums;

/// <summary>Stored as NVARCHAR in Transactions.TransactionType.</summary>
public enum TransactionType
{
    /// <summary>Purchase at a merchant - reduces the available balance.</summary>
    Swipe,
    /// <summary>Repayment / balance load by the cardholder - increases the available balance.</summary>
    Load,
    /// <summary>Merchant refund of an earlier swipe - increases the available balance.</summary>
    Refund,
    /// <summary>Payment of one EMI installment (Module 4) - frees the principal part of the credit limit.</summary>
    EmiInstallment,
    /// <summary>Module 8: a fee charged by the bank (late payment, cash advance) - increases what is owed.</summary>
    Fee,
    /// <summary>Module 8: finance charges on an unpaid balance or on cash.</summary>
    Interest,
    /// <summary>Module 8: GST on fees and interest.</summary>
    Tax
}
