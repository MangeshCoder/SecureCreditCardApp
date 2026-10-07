using Microsoft.Extensions.Options;
using SecureEmiCard.Application.Features.Emi;
using SecureEmiCard.Domain.Common;

namespace SecureEmiCard.UnitTests.Application;

public class EmiCalculatorTests
{
    private static readonly DateOnly Start = new(2026, 10, 7);

    private static EmiCalculator Calculator(EmiOptions? options = null) => new(Options.Create(options ?? new EmiOptions()));

    [Fact]
    public void Matches_the_standard_emi_formula()
    {
        // 1,00,000 for 12 months at 15 % p.a. -> EMI 9,025.83 (the figure any bank's EMI calculator shows)
        var calc = Calculator().Calculate(100_000m, 12, Start);

        Assert.Equal(15m, calc.AnnualInterestRate);
        Assert.Equal(9_025.83m, calc.MonthlyInstallment);
        Assert.Equal(12, calc.Schedule.Count);
        Assert.Equal(1_250.00m, calc.Schedule[0].Interest);     // 1,00,000 x 1.25 %
        Assert.Equal(7_775.83m, calc.Schedule[0].Principal);
    }

    [Theory]
    [InlineData(12_000, 3)]
    [InlineData(12_000, 6)]
    [InlineData(999.99, 12)]
    [InlineData(250_000, 24)]
    [InlineData(101, 24)]
    public void Principal_parts_add_up_to_exactly_the_amount(decimal amount, int tenure)
    {
        var calc = Calculator().Calculate(amount, tenure, Start);

        Assert.Equal(amount, calc.Schedule.Sum(l => l.Principal));                  // not a paisa more or less
        Assert.Equal(calc.TotalRepayable, calc.Schedule.Sum(l => l.Amount));
        Assert.Equal(calc.TotalRepayable - amount, calc.TotalInterest);
        Assert.All(calc.Schedule, l => Assert.True(l.Principal >= 0 && l.Interest >= 0));
        Assert.All(calc.Schedule.SkipLast(1), l => Assert.Equal(calc.MonthlyInstallment, l.Amount));
        // the last installment only differs by rounding
        Assert.InRange(calc.Schedule[^1].Amount - calc.MonthlyInstallment, -1m, 1m);
    }

    [Fact]
    public void Interest_falls_and_principal_rises_every_month()
    {
        var s = Calculator().Calculate(50_000m, 24, Start).Schedule;
        for (int i = 1; i < s.Count - 1; i++)
        {
            Assert.True(s[i].Interest <= s[i - 1].Interest);
            Assert.True(s[i].Principal >= s[i - 1].Principal);
        }
    }

    [Fact]
    public void Zero_percent_emi_splits_evenly_and_the_last_month_takes_the_rounding()
    {
        var options = new EmiOptions();
        options.AnnualInterestRates[3] = 0m;

        var calc = Calculator(options).Calculate(100m, 3, Start);

        Assert.Equal(new[] { 33.33m, 33.33m, 33.34m }, calc.Schedule.Select(l => l.Amount));
        Assert.Equal(0m, calc.TotalInterest);
    }

    [Fact]
    public void Due_dates_follow_the_start_date_without_drifting()
    {
        var s = Calculator().Calculate(10_000m, 3, new DateOnly(2027, 1, 31)).Schedule;
        Assert.Equal(new[] { new DateOnly(2027, 2, 28), new DateOnly(2027, 3, 31), new DateOnly(2027, 4, 30) },
                     s.Select(l => l.DueDate));
    }

    [Fact]
    public void Only_configured_tenures_are_offered()
    {
        Assert.Equal(new[] { 3, 6, 12, 24 }, Calculator().AvailableTenures);
        Assert.Throws<DomainException>(() => Calculator().Calculate(10_000m, 9, Start));
    }
}
