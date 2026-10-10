using SecureEmiCard.Domain.Common;
using SecureEmiCard.Domain.Enums;

namespace SecureEmiCard.Domain.Entities;

/// <summary>What happened to a statement on its due date.</summary>
public sealed record StatementAssessment(StatementStatus Status, decimal PaidByDueDate, decimal UnpaidBalance, decimal PastDueMinimum);

/// <summary>
/// A monthly card statement (Module 8). Maps to dbo.CardStatements. Once generated, its figures never change;
/// only the outcome on the due date (status, amount paid by then) is filled in later.
///
/// The figures always reconcile:
///   Opening + Purchases + CashWithdrawals + FeesAndCharges − Payments − Refunds − Cashback − MovedToEmi = ClosingBalance
/// ClosingBalance is the "Total amount due" (negative = credit balance). EMI principal is not part of it: EMI
/// installments are paid separately (Module 4).
/// </summary>
public class CardStatement
{
    // Required by EF Core
    private CardStatement() { }

    public static CardStatement Create(int cardId, DateTime periodStartUtc, DateTime periodEndUtc, DateTime dueDateUtc,
                                       StatementFigures figures, decimal minimumDue, decimal emiInstallmentsDue)
    {
        if (periodEndUtc <= periodStartUtc) throw new DomainException("The statement period must end after it starts.");
        if (dueDateUtc <= periodEndUtc) throw new DomainException("The due date must be after the statement date.");
        if (minimumDue < 0 || minimumDue > Math.Max(0, figures.ClosingBalance))
            throw new DomainException("The minimum amount due must be between 0 and the total amount due.");

        var statement = new CardStatement
        {
            CardId = cardId,
            PeriodStart = periodStartUtc,
            PeriodEnd = periodEndUtc,
            DueDate = dueDateUtc,
            OpeningBalance = figures.OpeningBalance,
            Purchases = figures.Purchases,
            CashWithdrawals = figures.CashWithdrawals,
            FeesAndCharges = figures.FeesAndCharges,
            Payments = figures.Payments,
            Refunds = figures.Refunds,
            Cashback = figures.Cashback,
            MovedToEmi = figures.MovedToEmi,
            ClosingBalance = figures.ClosingBalance,
            MinimumDue = minimumDue,
            EmiInstallmentsDue = emiInstallmentsDue,
            Status = StatementStatus.Open
        };

        // Nothing to pay: the statement is settled the moment it is created.
        if (figures.ClosingBalance <= 0)
        {
            statement.Status = StatementStatus.Paid;
            statement.PaidByDueDate = 0;
            statement.AssessedAt = periodEndUtc;
        }
        return statement;
    }

    public int StatementId { get; private set; }
    public int CardId { get; private set; }
    public DateTime PeriodStart { get; private set; }
    /// <summary>The statement date: everything up to this moment is on this statement.</summary>
    public DateTime PeriodEnd { get; private set; }
    public DateTime DueDate { get; private set; }

    public decimal OpeningBalance { get; private set; }
    public decimal Purchases { get; private set; }
    public decimal CashWithdrawals { get; private set; }
    public decimal FeesAndCharges { get; private set; }
    public decimal Payments { get; private set; }
    public decimal Refunds { get; private set; }
    public decimal Cashback { get; private set; }
    public decimal MovedToEmi { get; private set; }
    /// <summary>Total amount due.</summary>
    public decimal ClosingBalance { get; private set; }
    public decimal MinimumDue { get; private set; }
    /// <summary>EMI installments falling due by the due date (paid separately, shown for information).</summary>
    public decimal EmiInstallmentsDue { get; private set; }

    public StatementStatus Status { get; private set; }
    public decimal? PaidByDueDate { get; private set; }
    public DateTime? AssessedAt { get; private set; }
    public DateTime? ReminderSentAt { get; private set; }

    public CreditCard? Card { get; private set; }

    public bool IsAssessed => AssessedAt is not null;

    /// <summary>
    /// The due date has passed: record how much was paid in time and decide the outcome.
    /// Payments made after the statement date and up to the due date count.
    /// </summary>
    public StatementAssessment Assess(decimal paidByDueDate, DateTime nowUtc)
    {
        if (IsAssessed) throw new DomainException("This statement was already assessed.");
        if (nowUtc <= DueDate) throw new DomainException("The due date has not passed yet.");

        PaidByDueDate = Math.Max(0, paidByDueDate);
        AssessedAt = nowUtc;
        Status = PaidByDueDate >= ClosingBalance ? StatementStatus.Paid
               : PaidByDueDate >= MinimumDue ? StatementStatus.MinimumPaid
               : StatementStatus.Overdue;

        return new StatementAssessment(Status, PaidByDueDate.Value,
            UnpaidBalance: Math.Max(0, ClosingBalance - PaidByDueDate.Value),
            PastDueMinimum: Math.Max(0, MinimumDue - PaidByDueDate.Value));
    }

    public void MarkReminderSent(DateTime nowUtc) => ReminderSentAt ??= nowUtc;
}

/// <summary>The money figures of one statement period.</summary>
public sealed record StatementFigures(
    decimal OpeningBalance,
    decimal Purchases,
    decimal CashWithdrawals,
    decimal FeesAndCharges,
    decimal Payments,
    decimal Refunds,
    decimal Cashback,
    decimal MovedToEmi,
    decimal ClosingBalance)
{
    /// <summary>What the closing balance must be if every movement was counted - checked on every statement.</summary>
    public decimal ExpectedClosing =>
        OpeningBalance + Purchases + CashWithdrawals + FeesAndCharges - Payments - Refunds - Cashback - MovedToEmi;

    public bool Reconciles => ExpectedClosing == ClosingBalance;
}
