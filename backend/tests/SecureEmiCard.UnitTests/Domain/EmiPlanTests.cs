using Microsoft.Extensions.Options;
using SecureEmiCard.Application.Features.Emi;
using SecureEmiCard.Domain.Common;
using SecureEmiCard.Domain.Entities;
using SecureEmiCard.Domain.Enums;

namespace SecureEmiCard.UnitTests.Domain;

public class EmiPlanTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private static EmiPlan NewPlan(decimal amount = 12_000m, int tenure = 3)
    {
        var purchase = CardTransaction.ApprovedSwipe(1, "Croma", "5732", amount);
        purchase.MarkEmiConverted(100m, 30, Now);
        var calc = new EmiCalculator(Options.Create(new EmiOptions())).Calculate(amount, tenure, DateOnly.FromDateTime(Now));
        return EmiPlan.Create(purchase, tenure, calc.AnnualInterestRate, calc.MonthlyInstallment, calc.Schedule);
    }

    private static CardTransaction Payment(EmiPlan plan, int n) =>
        CardTransaction.EmiInstallment(1, $"EMI {n}", plan.Schedules.Single(s => s.InstallmentNumber == n).AmountDue);

    [Theory]
    [InlineData(100.00, false)]   // spec: "Amount > 100" - exactly 100 is not eligible
    [InlineData(100.01, true)]
    public void Minimum_amount_rule(decimal amount, bool eligible)
    {
        var swipe = CardTransaction.ApprovedSwipe(1, "Shop", "5411", amount);
        Assert.Equal(eligible, swipe.GetEmiIneligibilityReason(100m, 30, Now) is null);
    }

    [Fact]
    public void Refunded_old_converted_or_non_purchase_transactions_are_not_eligible()
    {
        var refunded = CardTransaction.ApprovedSwipe(1, "Shop", "5411", 500m);
        refunded.Refund();
        Assert.NotNull(refunded.GetEmiIneligibilityReason(100m, 30, Now));

        var old = CardTransaction.ApprovedSwipe(1, "Shop", "5411", 500m);
        Assert.NotNull(old.GetEmiIneligibilityReason(100m, 30, Now.AddDays(31))); // 31 days later

        var converted = CardTransaction.ApprovedSwipe(1, "Shop", "5411", 500m);
        converted.MarkEmiConverted(100m, 30, Now);
        Assert.Throws<DomainException>(() => converted.MarkEmiConverted(100m, 30, Now));
        Assert.Throws<DomainException>(() => converted.Refund());           // converted purchases cannot be refunded

        Assert.NotNull(CardTransaction.Load(1, 500m).GetEmiIneligibilityReason(100m, 30, Now));
        Assert.NotNull(CardTransaction.DeclinedSwipe(1, "Shop", "5411", 500m, "x").GetEmiIneligibilityReason(100m, 30, Now));
    }

    [Fact]
    public void Installments_are_paid_in_order_once_and_the_plan_closes()
    {
        var plan = NewPlan();
        Assert.Equal(plan.TotalRepayable, plan.RemainingBalance);

        Assert.Throws<DomainException>(() => plan.PayInstallment(2, Payment(plan, 2), Now));   // out of order

        plan.PayInstallment(1, Payment(plan, 1), Now);
        Assert.Throws<DomainException>(() => plan.PayInstallment(1, Payment(plan, 1), Now));   // already paid

        plan.PayInstallment(2, Payment(plan, 2), Now);
        plan.PayInstallment(3, Payment(plan, 3), Now);

        Assert.Equal(EmiPlanStatus.Closed, plan.PlanStatus);
        Assert.Equal(0m, plan.RemainingBalance);
        Assert.Equal(0m, plan.OutstandingPrincipal);
        Assert.Throws<DomainException>(() => plan.PayInstallment(3, Payment(plan, 3), Now));
    }

    [Fact]
    public void Payment_must_match_the_installment_amount()
    {
        var plan = NewPlan();
        Assert.Throws<DomainException>(() => plan.PayInstallment(1, CardTransaction.EmiInstallment(1, "EMI 1", 1m), Now));
    }
}
