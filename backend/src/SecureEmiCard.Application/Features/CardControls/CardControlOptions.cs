namespace SecureEmiCard.Application.Features.CardControls;

/// <summary>
/// Bank-wide rules behind the card controls (Module 6). Optional "CardControls" config section:
/// <code>
/// "CardControls": {
///   "HomeCountryCode": "IN",
///   "ContactlessPerTransactionLimit": 5000,
///   "BusinessDayUtcOffset": "05:30:00"
/// }
/// </code>
/// </summary>
public class CardControlOptions
{
    public const string SectionName = "CardControls";

    /// <summary>The issuer's country (ISO 3166 alpha-2). A merchant anywhere else is "international".</summary>
    public string HomeCountryCode { get; set; } = "IN";

    /// <summary>
    /// Highest amount for one tap-to-pay purchase. RBI allows contactless payments without a PIN up to
    /// 5,000 rupees; above that the customer must insert the card and enter the PIN.
    /// </summary>
    public decimal ContactlessPerTransactionLimit { get; set; } = 5_000m;

    /// <summary>
    /// When "today" starts for the daily limits: midnight in the bank's time zone. India has no daylight
    /// saving time, so a fixed offset (IST = UTC+05:30) is exact and works the same on Windows and Linux.
    /// </summary>
    public TimeSpan BusinessDayUtcOffset { get; set; } = new(5, 30, 0);
}
