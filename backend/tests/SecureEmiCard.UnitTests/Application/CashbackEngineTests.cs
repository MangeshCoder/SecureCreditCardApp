using Microsoft.Extensions.Options;
using SecureEmiCard.Application.Features.Cashback;

namespace SecureEmiCard.UnitTests.Application;

public class CashbackEngineTests
{
    private static CashbackEngine Engine(CashbackOptions? options = null) =>
        new(Options.Create(options ?? new CashbackOptions()));

    [Theory]
    [InlineData("5411", 1_000, 3.0, 30.00)]   // groceries 3 %
    [InlineData("5812", 1_000, 3.0, 30.00)]   // dining 3 %
    [InlineData("5541", 1_000, 2.0, 20.00)]   // fuel 2 %
    [InlineData("5732", 1_000, 1.0, 10.00)]   // electronics - no rule, default 1 %
    [InlineData("9999", 1_000, 1.0, 10.00)]   // unknown category - default 1 %
    public void Uses_the_category_rate_or_the_default(string mcc, decimal amount, decimal percentage, decimal cashback)
    {
        var quote = Engine().Calculate(amount, mcc);
        Assert.Equal(percentage, quote.Percentage);
        Assert.Equal(cashback, quote.Amount);
    }

    [Fact]
    public void No_cashback_below_the_minimum_spend()
    {
        Assert.Equal(CashbackQuote.None, Engine().Calculate(99.99m, "5411"));
        Assert.Equal(3.00m, Engine().Calculate(100m, "5411").Amount);
    }

    [Fact]
    public void Cashback_is_capped_per_transaction()
    {
        // 3 % of 50,000 = 1,500 but the cap is 500
        Assert.Equal(500m, Engine().Calculate(50_000m, "5411").Amount);
    }

    [Fact]
    public void Rounds_half_away_from_zero_to_two_decimals()
    {
        // 1 % of 100.50 = 1.005 → 1.01 (banker's rounding would give 1.00)
        Assert.Equal(1.01m, Engine().Calculate(100.50m, "5999").Amount);
    }

    [Fact]
    public void Rules_come_from_configuration()
    {
        var options = new CashbackOptions { DefaultPercentage = 0m, MinimumSpend = 0m, MaxCashbackPerTransaction = 50m };
        options.CategoryPercentages["5732"] = 5m; // electronics promotion

        var engine = Engine(options);
        Assert.Equal(25m, engine.Calculate(500m, "5732").Amount);
        Assert.Equal(CashbackQuote.None, engine.Calculate(500m, "5999")); // default 0 % → nothing
    }
}
