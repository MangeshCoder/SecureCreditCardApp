namespace SecureEmiCard.Domain.Common;

/// <summary>
/// ISO 18245 Merchant Category Codes (MCC) used by the application.
/// Module 3 (Cashback) uses them to decide the reward percentage.
/// </summary>
public static class MerchantCategoryCodes
{
    public const string Groceries = "5411";
    public const string Restaurants = "5812";
    public const string FuelStations = "5541";
    public const string DepartmentStores = "5311";
    public const string Electronics = "5732";
    public const string Travel = "4722";
    public const string MiscellaneousRetail = "5999";
    public const string FinancialInstitution = "6012";
    /// <summary>ATM cash withdrawal (Module 6). Only valid with the Atm channel.</summary>
    public const string CashWithdrawal = "6011";

    public static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>
    {
        [Groceries] = "Groceries & supermarkets",
        [Restaurants] = "Restaurants & dining",
        [FuelStations] = "Fuel stations",
        [DepartmentStores] = "Department stores",
        [Electronics] = "Electronics",
        [Travel] = "Travel agencies",
        [MiscellaneousRetail] = "Other retail",
        [CashWithdrawal] = "ATM cash withdrawal",
        [FinancialInstitution] = "Financial institution (repayments)"
    };
}
