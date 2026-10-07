using SecureEmiCard.Domain.Common;
using SecureEmiCard.Domain.Enums;

namespace SecureEmiCard.Domain.Entities;

/// <summary>One monthly installment of an EMI plan. Maps to table dbo.EmiSchedules.</summary>
public class EmiSchedule
{
    // Required by EF Core
    private EmiSchedule() { }

    internal EmiSchedule(int installmentNumber, DateOnly dueDate, decimal principal, decimal interest)
    {
        InstallmentNumber = installmentNumber;
        DueDate = dueDate;
        PrincipalComponent = principal;
        InterestComponent = interest;
        AmountDue = principal + interest;
        PaymentStatus = InstallmentStatus.Pending;
    }

    public int ScheduleId { get; private set; }
    public int EmiPlanId { get; private set; }
    public int InstallmentNumber { get; private set; }
    public DateOnly DueDate { get; private set; }
    public decimal AmountDue { get; private set; }
    public decimal PrincipalComponent { get; private set; }
    public decimal InterestComponent { get; private set; }
    public InstallmentStatus PaymentStatus { get; private set; }
    public DateTime? PaidDate { get; private set; }
    public int? PaymentTransactionId { get; private set; }

    /// <summary>Ledger row of the payment; EF fills PaymentTransactionId from it on save.</summary>
    public CardTransaction? PaymentTransaction { get; private set; }

    public bool IsPaid => PaymentStatus == InstallmentStatus.Paid;

    public bool IsOverdue(DateOnly today) => !IsPaid && DueDate < today;

    internal void MarkPaid(CardTransaction payment, DateTime paidAtUtc)
    {
        if (IsPaid) throw new DomainException($"Installment {InstallmentNumber} is already paid.");
        PaymentStatus = InstallmentStatus.Paid;
        PaidDate = paidAtUtc;
        PaymentTransaction = payment;
    }
}
