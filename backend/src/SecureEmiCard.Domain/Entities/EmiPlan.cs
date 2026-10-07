using SecureEmiCard.Domain.Common;
using SecureEmiCard.Domain.Enums;

namespace SecureEmiCard.Domain.Entities;

/// <summary>One line of a calculated amortization schedule (input for <see cref="EmiPlan.Create"/>).</summary>
public record EmiScheduleLine(int InstallmentNumber, DateOnly DueDate, decimal Principal, decimal Interest)
{
    public decimal Amount => Principal + Interest;
}

/// <summary>
/// A purchase converted into fixed monthly installments. Maps to table dbo.EmiPlans.
/// This is an AGGREGATE ROOT: the schedule rows are only changed through the plan, so rules like
/// "pay installments in order" and "close the plan after the last one" can never be bypassed.
/// </summary>
public class EmiPlan
{
    public static readonly IReadOnlyList<int> AllowedTenures = new[] { 3, 6, 12, 24 };

    private readonly List<EmiSchedule> _schedules = new();

    // Required by EF Core
    private EmiPlan() { }

    public int EmiPlanId { get; private set; }
    public int TransactionId { get; private set; }
    public int CardId { get; private set; }
    public decimal PrincipalAmount { get; private set; }
    public int TenureMonths { get; private set; }
    public decimal AnnualInterestRate { get; private set; }
    public decimal MonthlyInstallment { get; private set; }
    public decimal TotalRepayable { get; private set; }
    /// <summary>Amount still to be paid on this plan (sum of unpaid installments).</summary>
    public decimal RemainingBalance { get; private set; }
    public EmiPlanStatus PlanStatus { get; private set; }
    public DateTime CreatedDate { get; private set; }

    public CardTransaction? Transaction { get; private set; }
    public IReadOnlyCollection<EmiSchedule> Schedules => _schedules.AsReadOnly();

    public decimal TotalInterest => TotalRepayable - PrincipalAmount;

    /// <summary>Principal not yet repaid: this part of the credit limit is still in use.</summary>
    public decimal OutstandingPrincipal => _schedules.Where(s => !s.IsPaid).Sum(s => s.PrincipalComponent);

    public int PaidInstallments => _schedules.Count(s => s.IsPaid);

    public EmiSchedule? NextInstallment =>
        _schedules.Where(s => !s.IsPaid).OrderBy(s => s.InstallmentNumber).FirstOrDefault();

    /// <summary>
    /// Creates the plan for a purchase. The purchase must already be marked as converted
    /// (<see cref="CardTransaction.MarkEmiConverted"/>), and the schedule must repay exactly the principal.
    /// </summary>
    public static EmiPlan Create(CardTransaction purchase, int tenureMonths, decimal annualInterestRate,
                                 decimal monthlyInstallment, IReadOnlyList<EmiScheduleLine> schedule)
    {
        if (!purchase.IsEmiConverted) throw new DomainException("The purchase must be marked as converted first.");
        if (!AllowedTenures.Contains(tenureMonths)) throw new DomainException("Tenure must be 3, 6, 12 or 24 months.");
        if (schedule.Count != tenureMonths) throw new DomainException("The schedule must have one line per month.");
        if (schedule.Sum(l => l.Principal) != purchase.Amount)
            throw new DomainException("The schedule must repay exactly the purchase amount.");

        var plan = new EmiPlan
        {
            Transaction = purchase,
            TransactionId = purchase.TransactionId,
            CardId = purchase.CardId,
            PrincipalAmount = purchase.Amount,
            TenureMonths = tenureMonths,
            AnnualInterestRate = annualInterestRate,
            MonthlyInstallment = monthlyInstallment,
            TotalRepayable = schedule.Sum(l => l.Amount),
            RemainingBalance = schedule.Sum(l => l.Amount),
            PlanStatus = EmiPlanStatus.Active,
            CreatedDate = DateTime.UtcNow
        };
        foreach (var line in schedule.OrderBy(l => l.InstallmentNumber))
            plan._schedules.Add(new EmiSchedule(line.InstallmentNumber, line.DueDate, line.Principal, line.Interest));
        return plan;
    }

    /// <summary>
    /// Pays installment <paramref name="installmentNumber"/>. Installments must be paid in order, and the
    /// caller names the installment explicitly so a repeated request (double click, retry) cannot pay the
    /// following installment by accident.
    /// </summary>
    public EmiSchedule PayInstallment(int installmentNumber, CardTransaction payment, DateTime paidAtUtc)
    {
        if (PlanStatus == EmiPlanStatus.Closed) throw new DomainException("This EMI plan is already closed.");

        var installment = _schedules.SingleOrDefault(s => s.InstallmentNumber == installmentNumber)
                          ?? throw new DomainException($"Installment {installmentNumber} does not exist.");
        if (installment.IsPaid) throw new DomainException($"Installment {installmentNumber} is already paid.");
        if (NextInstallment!.InstallmentNumber != installmentNumber)
            throw new DomainException($"Please pay installment {NextInstallment.InstallmentNumber} first.");
        if (payment.Amount != installment.AmountDue)
            throw new DomainException("Payment amount must equal the installment amount.");

        installment.MarkPaid(payment, paidAtUtc);
        RemainingBalance -= installment.AmountDue;
        if (NextInstallment is null) PlanStatus = EmiPlanStatus.Closed;
        return installment;
    }
}
