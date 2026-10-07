namespace SecureEmiCard.Application.Features.Emi;

/// <summary>
/// EMI rules set by the BANK. Defaults below; override in configuration under "Emi":
/// <code>
/// "Emi": {
///   "MinimumAmount": 100,
///   "ConversionWindowDays": 30,
///   "AnnualInterestRates": { "3": 13.0, "6": 14.0, "12": 15.0, "24": 16.0 }
/// }
/// </code>
/// </summary>
public class EmiOptions
{
    public const string SectionName = "Emi";

    /// <summary>Only purchases ABOVE this amount can be converted (spec: "Amount &gt; 100").</summary>
    public decimal MinimumAmount { get; set; } = 100m;

    /// <summary>A purchase can be converted only within this many days.</summary>
    public int ConversionWindowDays { get; set; } = 30;

    /// <summary>Annual interest rate (%) per tenure in months. The keys are the tenures on offer.</summary>
    public Dictionary<int, decimal> AnnualInterestRates { get; set; } = new()
    {
        [3] = 13.0m,
        [6] = 14.0m,
        [12] = 15.0m,
        [24] = 16.0m
    };
}
