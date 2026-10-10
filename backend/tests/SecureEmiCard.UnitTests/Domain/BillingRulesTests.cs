using Microsoft.Extensions.Options;
using SecureEmiCard.Application.Features.Billing;
using SecureEmiCard.Domain.Common;
using SecureEmiCard.Domain.Entities;
using SecureEmiCard.Domain.Enums;

namespace SecureEmiCard.UnitTests.Domain;

/// <summary>Module 8: the billing maths and the statement's life cycle - no database, no clock.</summary>
public class BillingRulesTests
{
    private readonly BillingCalculator _calc = new(Options.Create(new BillingOptions()));
    private static readonly DateTime T0 = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(10_000, 0, 0, 500)]          // 5 %
    [InlineData(2_000, 0, 0, 200)]           // 5 % = 100 → the ₹200 floor
    [InlineData(150, 0, 0, 150)]             // never more than the total due
    [InlineData(10_000, 708, 0, 1_172.60)]   // 5 % of 9,292 + all charges
    [InlineData(10_000, 0, 495, 995)]        // + the minimum left unpaid last time
    [InlineData(-50, 0, 0, 0)]               // credit balance: nothing to pay
    public void Minimum_due(decimal total, decimal charges, decimal pastDue, decimal expected) =>
        Assert.Equal(expected, _calc.MinimumDue(total, charges, pastDue));

    [Theory]
    [InlineData(100, 0)]
    [InlineData(100.01, 100)]
    [InlineData(500, 100)]
    [InlineData(3_000, 500)]
    [InlineData(9_900, 600)]
    [InlineData(20_000, 800)]
    [InlineData(50_000, 1_000)]
    [InlineData(60_000, 1_200)]
    public void Late_fee_follows_the_slabs(decimal unpaid, decimal fee) => Assert.Equal(fee, _calc.LateFee(unpaid));

    [Theory]
    [InlineData(10_000, 0, 0, 350)]          // nothing paid: 3.5 % of everything
    [InlineData(10_000, 0, 2_000, 280)]      // on the unpaid 8,000
    [InlineData(10_000, 0, 10_000, 0)]       // paid in full: no interest
    [InlineData(10_000, 708, 0, 325.22)]     // never on unpaid charges: 3.5 % of 9,292
    [InlineData(10_000, 708, 500, 325.22)]   // a payment smaller than the charges pays the charges first
    public void Interest_only_on_unpaid_principal(decimal total, decimal charges, decimal paid, decimal expected) =>
        Assert.Equal(expected, _calc.Interest(total, charges, paid));

    [Fact]
    public void Cash_has_a_minimum_fee_interest_from_day_one_and_gst_on_charges()
    {
        Assert.Equal(500m, _calc.CashAdvanceFee(2_000m));                   // 2.5 % = 50 → minimum 500
        Assert.Equal(1_000m, _calc.CashAdvanceFee(40_000m));
        var withdrawn = new DateOnly(2026, 10, 1);
        Assert.Equal(116.67m, _calc.CashInterest(10_000m, withdrawn, withdrawn.AddDays(10)));
        Assert.Equal(11.67m, _calc.CashInterest(10_000m, withdrawn, withdrawn));     // same day: still one day
        Assert.Equal(90m, _calc.Gst(500m));
        Assert.Equal(58.54m, _calc.Gst(325.22m));
    }

    private static CardStatement Statement(decimal closing, decimal minimum, decimal charges = 0m) =>
        CardStatement.Create(1, T0.AddDays(-30), T0, T0.AddDays(20),
            new StatementFigures(0, closing - charges, 0, charges, 0, 0, 0, 0, closing), minimum, 0);

    [Theory]
    [InlineData(9_900, StatementStatus.Paid, 0, 0)]
    [InlineData(495, StatementStatus.MinimumPaid, 9_405, 0)]
    [InlineData(100, StatementStatus.Overdue, 9_800, 395)]
    public void Outcome_on_the_due_date(decimal paid, StatementStatus status, decimal unpaid, decimal pastDue)
    {
        var statement = Statement(9_900m, 495m);
        var outcome = statement.Assess(paid, T0.AddDays(21));

        Assert.Equal(status, outcome.Status);
        Assert.Equal(unpaid, outcome.UnpaidBalance);
        Assert.Equal(pastDue, outcome.PastDueMinimum);
        Assert.Throws<DomainException>(() => statement.Assess(paid, T0.AddDays(22)));     // decided once
    }

    [Fact]
    public void Outcome_cannot_be_decided_before_the_due_date_and_nothing_due_is_settled_at_once()
    {
        Assert.Throws<DomainException>(() => Statement(9_900m, 495m).Assess(9_900m, T0.AddDays(20)));

        var credit = Statement(-200m, 0m);
        Assert.Equal(StatementStatus.Paid, credit.Status);
        Assert.True(credit.IsAssessed);
    }

    [Fact]
    public void Figures_must_add_up()
    {
        var ok = new StatementFigures(1_000, 24_000, 3_000, 594.13m, 1_000, 2_000, 220, 12_000, 13_374.13m);
        Assert.True(ok.Reconciles);
        Assert.False((ok with { ClosingBalance = 13_374.12m }).Reconciles);
    }

    [Fact]
    public void Charges_may_take_the_card_over_its_limit_which_stops_purchases()
    {
        var card = new CreditCard(1, "enc", "hash", "XXXX-XXXX-XXXX-1234", "cvv", "pin", 1_000m,
                                  DateOnly.FromDateTime(DateTime.UtcNow.AddYears(5)));
        card.Debit(1_000m);
        card.ApplyCharge(590m);

        Assert.Equal(-590m, card.AvailableBalance);
        Assert.Equal(1_590m, card.OutstandingAmount);
        Assert.Equal(DeclineReasons.InsufficientCredit, card.GetSwipeDeclineReason(1m));
        Assert.Throws<DomainException>(() => CardTransaction.Charge(1, TransactionType.Swipe, "x", 10m, T0));
    }
}
