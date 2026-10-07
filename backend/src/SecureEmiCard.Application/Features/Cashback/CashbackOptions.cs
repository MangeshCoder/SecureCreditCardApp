using SecureEmiCard.Domain.Common;

namespace SecureEmiCard.Application.Features.Cashback;

/// <summary>
/// Cashback rules. The defaults below are the specification's rules; any value can be overridden
/// in configuration (appsettings / environment variables / Key Vault) under the "Cashback" section:
/// <code>
/// "Cashback": {
///   "DefaultPercentage": 1.0,
///   "MinimumSpend": 100,
///   "MaxCashbackPerTransaction": 500,
///   "CategoryPercentages": { "5411": 3.0, "5812": 3.0, "5541": 2.0 }
/// }
/// </code>
/// </summary>
public class CashbackOptions
{
    public const string SectionName = "Cashback";

    /// <summary>Percentage for merchant categories that have no specific rule (1 %).</summary>
    public decimal DefaultPercentage { get; set; } = 1.0m;

    /// <summary>Swipes below this amount earn no cashback.</summary>
    public decimal MinimumSpend { get; set; } = 100m;

    /// <summary>Upper limit of cashback for a single swipe.</summary>
    public decimal MaxCashbackPerTransaction { get; set; } = 500m;

    /// <summary>Percentage per Merchant Category Code.</summary>
    public Dictionary<string, decimal> CategoryPercentages { get; set; } = new()
    {
        [MerchantCategoryCodes.Groceries] = 3.0m,
        [MerchantCategoryCodes.Restaurants] = 3.0m,
        [MerchantCategoryCodes.FuelStations] = 2.0m
    };
}
