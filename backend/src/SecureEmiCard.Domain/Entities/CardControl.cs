using SecureEmiCard.Domain.Common;
using SecureEmiCard.Domain.Enums;

namespace SecureEmiCard.Domain.Entities;

/// <summary>A channel switch plus its optional daily limit (null = no extra limit, only the available credit).</summary>
public sealed record ChannelSetting(bool Enabled, decimal? DailyLimit);

/// <summary>
/// The cardholder's own controls for one card (Module 6). Maps to table dbo.CardControls, one row per card.
///
/// Defaults follow the RBI rule for new cards: usable only at contact-based points in India (chip + PIN
/// at shops and ATMs). Online, contactless and international use stay off until the cardholder turns them on.
/// </summary>
public class CardControl
{
    // Required by EF Core
    private CardControl() { }

    public static CardControl CreateDefault() => new()
    {
        PosEnabled = true,
        AtmEnabled = true,
        UpdatedAt = DateTime.UtcNow
    };

    public int CardId { get; private set; }

    public bool PosEnabled { get; private set; }
    public bool OnlineEnabled { get; private set; }
    public bool ContactlessEnabled { get; private set; }
    public bool AtmEnabled { get; private set; }
    public bool InternationalEnabled { get; private set; }

    public decimal? PosDailyLimit { get; private set; }
    public decimal? OnlineDailyLimit { get; private set; }
    public decimal? ContactlessDailyLimit { get; private set; }
    public decimal? AtmDailyLimit { get; private set; }
    public decimal? InternationalDailyLimit { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public bool IsEnabled(TransactionChannel channel) => channel switch
    {
        TransactionChannel.Pos => PosEnabled,
        TransactionChannel.Online => OnlineEnabled,
        TransactionChannel.Contactless => ContactlessEnabled,
        TransactionChannel.Atm => AtmEnabled,
        _ => false
    };

    public decimal? DailyLimitFor(TransactionChannel channel) => channel switch
    {
        TransactionChannel.Pos => PosDailyLimit,
        TransactionChannel.Online => OnlineDailyLimit,
        TransactionChannel.Contactless => ContactlessDailyLimit,
        TransactionChannel.Atm => AtmDailyLimit,
        _ => null
    };

    /// <summary>
    /// Replaces all settings at once. Every limit is checked BEFORE anything changes,
    /// so an invalid request never leaves the controls half-updated.
    /// </summary>
    public void Update(ChannelSetting pos, ChannelSetting online, ChannelSetting contactless, ChannelSetting atm,
                       ChannelSetting international, decimal creditLimit)
    {
        EnsureValidLimit(pos, TransactionChannels.NameOf(TransactionChannel.Pos), creditLimit);
        EnsureValidLimit(online, TransactionChannels.NameOf(TransactionChannel.Online), creditLimit);
        EnsureValidLimit(contactless, TransactionChannels.NameOf(TransactionChannel.Contactless), creditLimit);
        EnsureValidLimit(atm, TransactionChannels.NameOf(TransactionChannel.Atm), creditLimit);
        EnsureValidLimit(international, TransactionChannels.InternationalName, creditLimit);

        (PosEnabled, PosDailyLimit) = (pos.Enabled, pos.DailyLimit);
        (OnlineEnabled, OnlineDailyLimit) = (online.Enabled, online.DailyLimit);
        (ContactlessEnabled, ContactlessDailyLimit) = (contactless.Enabled, contactless.DailyLimit);
        (AtmEnabled, AtmDailyLimit) = (atm.Enabled, atm.DailyLimit);
        (InternationalEnabled, InternationalDailyLimit) = (international.Enabled, international.DailyLimit);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Switch checks - no database needed, so they run before the PIN is checked.</summary>
    public string? GetUsageDeclineReason(TransactionChannel channel, bool isInternational)
    {
        if (!IsEnabled(channel)) return DeclineReasons.ChannelDisabled(channel);
        if (isInternational && !InternationalEnabled) return DeclineReasons.InternationalDisabled;
        return null;
    }

    /// <summary>
    /// Limit checks for a purchase of <paramref name="amount"/> on top of what was already spent today.
    /// A purchase that reaches the limit exactly is still allowed.
    /// </summary>
    public string? GetLimitDeclineReason(TransactionChannel channel, bool isInternational, decimal amount,
                                         DailySpend spentToday, decimal contactlessPerTransactionLimit)
    {
        if (channel == TransactionChannel.Contactless && amount > contactlessPerTransactionLimit)
            return DeclineReasons.ContactlessLimitExceeded;

        var channelLimit = DailyLimitFor(channel);
        if (channelLimit is not null && spentToday.For(channel) + amount > channelLimit)
            return DeclineReasons.DailyLimitExceeded(channel);

        if (isInternational && InternationalDailyLimit is not null && spentToday.International + amount > InternationalDailyLimit)
            return DeclineReasons.InternationalDailyLimitExceeded;

        return null;
    }

    private static void EnsureValidLimit(ChannelSetting setting, string name, decimal creditLimit)
    {
        if (setting.DailyLimit is null) return;
        if (setting.DailyLimit <= 0)
            throw new DomainException($"{name}: the daily limit must be greater than zero.");
        if (setting.DailyLimit > creditLimit)
            throw new DomainException($"{name}: the daily limit cannot be higher than the credit limit ({creditLimit:0.00}).");
    }
}
