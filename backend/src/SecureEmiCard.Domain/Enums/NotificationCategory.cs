namespace SecureEmiCard.Domain.Enums;

/// <summary>Stored as NVARCHAR in Notifications.Category.</summary>
public enum NotificationCategory
{
    /// <summary>Money movements: purchases, declines, repayments, refunds, EMI.</summary>
    Transaction,
    /// <summary>Card lifecycle: issued, blocked, credit limit.</summary>
    Card,
    /// <summary>Security-relevant changes: sign-in, PIN, card number viewed, lock, controls.</summary>
    Security,
    /// <summary>Module 8: statement ready, payment reminder, late fee / interest charged.</summary>
    Billing
}
