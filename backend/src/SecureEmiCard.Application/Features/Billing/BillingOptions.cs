namespace SecureEmiCard.Application.Features.Billing;

/// <summary>A late payment fee for an unpaid balance up to <see cref="UpTo"/> (null = everything above).</summary>
public class LateFeeSlab
{
    public decimal? UpTo { get; set; }
    public decimal Fee { get; set; }
}

/// <summary>
/// Billing rules (Module 8). Optional "Billing" config section; the values below are the defaults and are
/// typical for Indian credit cards (each bank publishes its own in the card's Most Important Terms and Conditions).
/// </summary>
public class BillingOptions
{
    public const string SectionName = "Billing";

    /// <summary>Length of a billing cycle: a statement is generated this long after the previous one.</summary>
    public TimeSpan CycleLength { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Statement date → payment due date (the interest-free "grace" period). 15-25 days in India.</summary>
    public TimeSpan PaymentDuePeriod { get; set; } = TimeSpan.FromDays(20);

    /// <summary>Minimum amount due = this % of the balance (without charges) + all charges, at least <see cref="MinimumDueFloor"/>.</summary>
    public decimal MinimumDuePercent { get; set; } = 5m;
    public decimal MinimumDueFloor { get; set; } = 200m;

    /// <summary>Finance charges per billing cycle on the unpaid balance (3.5 % a month ≈ 42 % a year).</summary>
    public decimal MonthlyInterestPercent { get; set; } = 3.5m;

    /// <summary>ATM cash: fee charged immediately, and interest from the day of withdrawal (no grace period).</summary>
    public decimal CashAdvanceFeePercent { get; set; } = 2.5m;
    public decimal CashAdvanceMinimumFee { get; set; } = 500m;

    /// <summary>GST on every fee and on interest.</summary>
    public decimal GstPercent { get; set; } = 18m;

    /// <summary>Late fee by unpaid balance. Empty = <see cref="BillingCalculator.DefaultLateFeeSlabs"/>.</summary>
    public List<LateFeeSlab> LateFeeSlabs { get; set; } = new();

    /// <summary>How often the scheduler looks for cards whose cycle ended, overdue statements and reminders.</summary>
    public TimeSpan SchedulerInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>"Payment due" reminder this long before the due date (if the minimum is not paid yet).</summary>
    public TimeSpan ReminderBeforeDue { get; set; } = TimeSpan.FromDays(3);
}
