using Microsoft.Extensions.Options;

namespace SecureEmiCard.Application.Features.Cashback;

/// <summary>Result of a cashback calculation. Amount 0 means "no cashback for this swipe".</summary>
public record CashbackQuote(decimal Percentage, decimal Amount)
{
    public static readonly CashbackQuote None = new(0m, 0m);
}

public interface ICashbackEngine
{
    CashbackQuote Calculate(decimal amount, string merchantCategoryCode);
    CashbackOptions Rules { get; }
}

/// <summary>
/// The rules engine from the specification (EmiEngineService.CalculateTransactionCashback),
/// moved into its own class and made configurable:
///   1. below the minimum spend          → no cashback
///   2. percentage = rule for the MCC, otherwise the default percentage
///   3. cashback   = amount × percentage / 100, rounded to 2 decimals (half away from zero)
///   4. capped at MaxCashbackPerTransaction
/// It is a pure calculation (no database, no clock), so it is trivial to unit test.
/// </summary>
public class CashbackEngine : ICashbackEngine
{
    public CashbackEngine(IOptions<CashbackOptions> options) => Rules = options.Value;

    public CashbackOptions Rules { get; }

    public CashbackQuote Calculate(decimal amount, string merchantCategoryCode)
    {
        if (amount < Rules.MinimumSpend) return CashbackQuote.None;

        var percentage = Rules.CategoryPercentages.TryGetValue(merchantCategoryCode, out var categoryPercentage)
            ? categoryPercentage
            : Rules.DefaultPercentage;
        if (percentage <= 0) return CashbackQuote.None;

        // AwayFromZero: 0.125 → 0.13, the rounding customers expect. (.NET's default is banker's rounding.)
        var cashback = Math.Round(amount * percentage / 100m, 2, MidpointRounding.AwayFromZero);
        cashback = Math.Min(cashback, Rules.MaxCashbackPerTransaction);

        return cashback > 0 ? new CashbackQuote(percentage, cashback) : CashbackQuote.None;
    }
}
