using System.Globalization;
using SecureEmiCard.Domain.Entities;

namespace SecureEmiCard.Application.Features.CardControls;

/// <summary>
/// One line describing the controls, e.g. "POS on (limit 5000.00) · Online on · ... · International off".
/// Used in the audit trail (Module 6) and in the "Card controls changed" alert (Module 7).
/// </summary>
public static class CardControlSummary
{
    public static string Describe(CardControl c) => Describe(
        ("POS", c.PosEnabled, c.PosDailyLimit), ("Online", c.OnlineEnabled, c.OnlineDailyLimit),
        ("Contactless", c.ContactlessEnabled, c.ContactlessDailyLimit), ("ATM", c.AtmEnabled, c.AtmDailyLimit),
        ("International", c.InternationalEnabled, c.InternationalDailyLimit));

    public static string Describe(CardControlsDto c) => Describe(
        ("POS", c.Pos.Enabled, c.Pos.DailyLimit), ("Online", c.Online.Enabled, c.Online.DailyLimit),
        ("Contactless", c.Contactless.Enabled, c.Contactless.DailyLimit), ("ATM", c.Atm.Enabled, c.Atm.DailyLimit),
        ("International", c.International.Enabled, c.International.DailyLimit));

    private static string Describe(params (string Name, bool Enabled, decimal? Limit)[] parts) =>
        string.Join(" · ", parts.Select(p =>
            $"{p.Name} {(p.Enabled ? "on" : "off")}" +
            (p.Limit is { } limit ? $" (limit {limit.ToString("0.00", CultureInfo.InvariantCulture)})" : "")));
}
