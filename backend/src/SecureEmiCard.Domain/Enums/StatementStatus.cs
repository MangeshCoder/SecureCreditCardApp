namespace SecureEmiCard.Domain.Enums;

/// <summary>Module 8. Stored as NVARCHAR in CardStatements.Status.</summary>
public enum StatementStatus
{
    /// <summary>The due date has not passed yet.</summary>
    Open,
    /// <summary>The total amount due was paid by the due date: no interest, no fee.</summary>
    Paid,
    /// <summary>At least the minimum was paid: no late fee, but interest on the unpaid part.</summary>
    MinimumPaid,
    /// <summary>Less than the minimum was paid: late fee + interest.</summary>
    Overdue
}
