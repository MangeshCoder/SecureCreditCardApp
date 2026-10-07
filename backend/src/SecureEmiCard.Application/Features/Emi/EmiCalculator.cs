using Microsoft.Extensions.Options;
using SecureEmiCard.Domain.Common;
using SecureEmiCard.Domain.Entities;

namespace SecureEmiCard.Application.Features.Emi;

public record EmiCalculation(
    decimal PrincipalAmount,
    int TenureMonths,
    decimal AnnualInterestRate,
    decimal MonthlyInstallment,
    decimal TotalInterest,
    decimal TotalRepayable,
    IReadOnlyList<EmiScheduleLine> Schedule);

public interface IEmiCalculator
{
    EmiOptions Rules { get; }
    IReadOnlyList<int> AvailableTenures { get; }
    decimal GetAnnualRate(int tenureMonths);
    EmiCalculation Calculate(decimal principal, int tenureMonths, DateOnly startDate);
}

/// <summary>
/// The amortization engine (spec §4B, EmiEngineService.CalculateEmiPlan), rewritten for exact money maths.
///
///   r   = annual rate / 12 / 100                      monthly interest rate
///   EMI = P · r · (1+r)^n / ((1+r)^n − 1)             reducing-balance formula
///   for each month: interest  = balance · r
///                   principal = EMI − interest
///                   balance  -= principal
///
/// Differences from the specification's code:
///  - decimal everywhere (the spec used double for (1+r)^n, which introduces binary rounding errors);
///  - every amount is rounded to 2 decimals (half away from zero);
///  - the LAST installment repays whatever principal is left, so the principal parts add up to exactly
///    the purchase amount (the spec's loop could leave a few paise unpaid or overpaid);
///  - 0 % interest is supported (the formula would divide by zero);
///  - the rate comes from the bank's configuration, never from the customer's request.
/// </summary>
public class EmiCalculator : IEmiCalculator
{
    public EmiCalculator(IOptions<EmiOptions> options) => Rules = options.Value;

    public EmiOptions Rules { get; }

    public IReadOnlyList<int> AvailableTenures =>
        Rules.AnnualInterestRates.Keys.Where(t => EmiPlan.AllowedTenures.Contains(t)).Order().ToList();

    public decimal GetAnnualRate(int tenureMonths) =>
        AvailableTenures.Contains(tenureMonths)
            ? Rules.AnnualInterestRates[tenureMonths]
            : throw new DomainException($"Tenure must be one of: {string.Join(", ", AvailableTenures)} months.");

    public EmiCalculation Calculate(decimal principal, int tenureMonths, DateOnly startDate)
    {
        if (principal <= 0) throw new DomainException("Principal amount must be greater than zero.");
        var annualRate = GetAnnualRate(tenureMonths);
        var r = annualRate / 12m / 100m;

        decimal emi;
        if (r == 0)
        {
            emi = Round(principal / tenureMonths);
        }
        else
        {
            var factor = Pow(1 + r, tenureMonths);             // (1+r)^n, exact decimal multiplication
            emi = Round(principal * r * factor / (factor - 1));
        }

        var lines = new List<EmiScheduleLine>(tenureMonths);
        var balance = principal;
        for (int month = 1; month <= tenureMonths; month++)
        {
            var interest = Round(balance * r);
            var principalPart = month == tenureMonths
                ? balance                                       // last month: clear the balance exactly
                : Math.Min(emi - interest, balance);
            balance -= principalPart;
            // Due dates are calculated from the start date each time (not month-to-month), so a plan
            // started on 31 January is due on 28/29 Feb, then 31 Mar - it does not drift to the 28th.
            lines.Add(new EmiScheduleLine(month, startDate.AddMonths(month), principalPart, interest));
        }

        var totalRepayable = lines.Sum(l => l.Amount);
        return new EmiCalculation(principal, tenureMonths, annualRate, emi, totalRepayable - principal, totalRepayable, lines);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static decimal Pow(decimal value, int exponent)
    {
        decimal result = 1m;
        for (int i = 0; i < exponent; i++) result *= value;
        return result;
    }
}
