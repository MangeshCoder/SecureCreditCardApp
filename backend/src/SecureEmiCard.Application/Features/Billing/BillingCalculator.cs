using Microsoft.Extensions.Options;

namespace SecureEmiCard.Application.Features.Billing;

/// <summary>
/// The billing maths (Module 8). Pure functions - no database, no clock - so every rule is easy to test.
/// All amounts are rounded to the paisa, half away from zero.
/// </summary>
public class BillingCalculator
{
    public static readonly IReadOnlyList<LateFeeSlab> DefaultLateFeeSlabs = new[]
    {
        new LateFeeSlab { UpTo = 100m, Fee = 0m },
        new LateFeeSlab { UpTo = 500m, Fee = 100m },
        new LateFeeSlab { UpTo = 5_000m, Fee = 500m },
        new LateFeeSlab { UpTo = 10_000m, Fee = 600m },
        new LateFeeSlab { UpTo = 25_000m, Fee = 800m },
        new LateFeeSlab { UpTo = 50_000m, Fee = 1_000m },
        new LateFeeSlab { UpTo = null, Fee = 1_200m }
    };

    public BillingCalculator(IOptions<BillingOptions> options)
    {
        Rules = options.Value;
        LateFeeSlabs = Rules.LateFeeSlabs.Count > 0 ? Rules.LateFeeSlabs : DefaultLateFeeSlabs;
    }

    public BillingOptions Rules { get; }
    public IReadOnlyList<LateFeeSlab> LateFeeSlabs { get; }

    /// <summary>
    /// Minimum amount due: <see cref="BillingOptions.MinimumDuePercent"/> of the balance without this cycle's
    /// charges, plus 100 % of those charges, plus any minimum left unpaid from earlier statements - but at least
    /// <see cref="BillingOptions.MinimumDueFloor"/> and never more than the total due.
    /// </summary>
    public decimal MinimumDue(decimal totalDue, decimal charges, decimal pastDue)
    {
        if (totalDue <= 0) return 0m;
        var percentPart = Round(Math.Max(0, totalDue - charges) * Rules.MinimumDuePercent / 100m);
        var minimum = Math.Max(Rules.MinimumDueFloor, percentPart + charges + pastDue);
        return Math.Min(totalDue, minimum);
    }

    /// <summary>Late fee for the balance left unpaid on the due date (RBI: on the overdue amount, not the whole bill).</summary>
    public decimal LateFee(decimal unpaidBalance)
    {
        if (unpaidBalance <= 0) return 0m;
        return LateFeeSlabs.First(s => s.UpTo is null || unpaidBalance <= s.UpTo).Fee;
    }

    /// <summary>
    /// Interest for one cycle on the part of the statement left unpaid. Payments are applied to the charges
    /// first, and interest is never charged on unpaid fees, interest or GST (RBI: no capitalisation of charges).
    /// </summary>
    public decimal Interest(decimal statementTotal, decimal statementCharges, decimal paid)
    {
        var principalUnpaid = statementTotal - Math.Max(paid, statementCharges);
        return principalUnpaid > 0 ? Round(principalUnpaid * Rules.MonthlyInterestPercent / 100m) : 0m;
    }

    /// <summary>ATM cash: a percentage of the amount, at least the minimum fee.</summary>
    public decimal CashAdvanceFee(decimal amount) =>
        Math.Max(Rules.CashAdvanceMinimumFee, Round(amount * Rules.CashAdvanceFeePercent / 100m));

    /// <summary>
    /// Interest on cash from the day it was taken until the statement date, counted in calendar days like a
    /// bank does - no interest-free period, so even same-day cash pays one day.
    /// </summary>
    public decimal CashInterest(decimal amount, DateOnly withdrawnOn, DateOnly statementDate)
    {
        var days = Math.Max(1, statementDate.DayNumber - withdrawnOn.DayNumber);
        return Round(amount * Rules.MonthlyInterestPercent / 100m / 30m * days);
    }

    public decimal Gst(decimal charge) => Round(charge * Rules.GstPercent / 100m);

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
