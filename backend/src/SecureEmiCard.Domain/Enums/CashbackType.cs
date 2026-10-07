namespace SecureEmiCard.Domain.Enums;

/// <summary>Stored as NVARCHAR in CashbackLogs.CashbackType.</summary>
public enum CashbackType
{
    /// <summary>Credited for an approved swipe (positive amount).</summary>
    Earned,
    /// <summary>Taken back because the swipe was refunded (negative amount).</summary>
    Reversed
}
