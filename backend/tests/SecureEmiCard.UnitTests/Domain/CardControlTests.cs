using SecureEmiCard.Application.Features.CardControls;
using SecureEmiCard.Domain.Common;
using SecureEmiCard.Domain.Entities;
using SecureEmiCard.Domain.Enums;

namespace SecureEmiCard.UnitTests.Domain;

/// <summary>Module 6 rules on the card and its controls - no database, no services.</summary>
public class CardControlTests
{
    private static CreditCard NewCard(decimal limit = 100_000m) =>
        new(1, "enc", "hash", "XXXX-XXXX-XXXX-1234", "cvv", "pin", limit, DateOnly.FromDateTime(DateTime.UtcNow.AddYears(5)));

    private static readonly ChannelSetting On = new(true, null);
    private static readonly ChannelSetting Off = new(false, null);

    private static DailySpend Spent(TransactionChannel channel, decimal amount, decimal international = 0m) =>
        new(new Dictionary<TransactionChannel, decimal> { [channel] = amount }, international);

    [Fact]
    public void New_card_follows_the_rbi_default_only_chip_and_pin_at_home()
    {
        var controls = NewCard().Controls!;

        Assert.True(controls.IsEnabled(TransactionChannel.Pos));
        Assert.True(controls.IsEnabled(TransactionChannel.Atm));
        Assert.False(controls.IsEnabled(TransactionChannel.Online));
        Assert.False(controls.IsEnabled(TransactionChannel.Contactless));
        Assert.False(controls.InternationalEnabled);
        Assert.All(Enum.GetValues<TransactionChannel>(), c => Assert.Null(controls.DailyLimitFor(c)));
    }

    [Fact]
    public void Switched_off_channel_and_international_use_are_declined()
    {
        var card = NewCard();

        Assert.Equal(DeclineReasons.ChannelDisabled(TransactionChannel.Online),
                     card.GetUsageDeclineReason(TransactionChannel.Online, isInternational: false));
        Assert.Equal(DeclineReasons.InternationalDisabled,
                     card.GetUsageDeclineReason(TransactionChannel.Pos, isInternational: true));
        Assert.Null(card.GetUsageDeclineReason(TransactionChannel.Pos, isInternational: false));
    }

    [Fact]
    public void Lock_stops_spending_but_not_repayments_until_unlocked()
    {
        var card = NewCard(10_000m);
        card.Debit(1_000m);

        card.Lock();
        Assert.Equal(DeclineReasons.CardLocked, card.GetUsageDeclineReason(TransactionChannel.Pos, false));
        Assert.Throws<DomainException>(() => card.Debit(10m));
        card.Credit(500m);                                        // paying the bill still works
        Assert.Equal(9_500m, card.AvailableBalance);

        card.Unlock();
        Assert.Null(card.GetUsageDeclineReason(TransactionChannel.Pos, false));
        Assert.Null(card.LockedAt);
    }

    [Fact]
    public void Lock_and_unlock_reject_the_wrong_state()
    {
        var card = NewCard();
        Assert.Throws<DomainException>(card.Unlock);              // not locked
        card.Lock();
        Assert.Throws<DomainException>(card.Lock);                // already locked

        var blocked = NewCard();
        blocked.Block();
        Assert.Throws<DomainException>(blocked.Lock);             // the bank's block wins
    }

    [Theory]
    [InlineData(400.00, true)]      // 600 + 400 = exactly the limit
    [InlineData(400.01, false)]
    public void Daily_limit_can_be_reached_but_not_exceeded(decimal amount, bool allowed)
    {
        var controls = NewCard().Controls!;
        controls.Update(new ChannelSetting(true, 1_000m), Off, Off, On, Off, creditLimit: 100_000m);

        var reason = controls.GetLimitDeclineReason(TransactionChannel.Pos, false, amount,
                                                    Spent(TransactionChannel.Pos, 600m), contactlessPerTransactionLimit: 5_000m);

        Assert.Equal(allowed ? null : DeclineReasons.DailyLimitExceeded(TransactionChannel.Pos), reason);
    }

    [Fact]
    public void International_limit_counts_every_channel_abroad()
    {
        var controls = NewCard().Controls!;
        controls.Update(On, On, Off, On, new ChannelSetting(true, 10_000m), creditLimit: 100_000m);

        // 9,000 already spent abroad (in any channel); 1,500 more online abroad is too much.
        var reason = controls.GetLimitDeclineReason(TransactionChannel.Online, true, 1_500m,
                                                    Spent(TransactionChannel.Pos, 9_000m, international: 9_000m), 5_000m);

        Assert.Equal(DeclineReasons.InternationalDailyLimitExceeded, reason);
        Assert.Null(controls.GetLimitDeclineReason(TransactionChannel.Online, false, 1_500m,       // same purchase at home
                                                   Spent(TransactionChannel.Pos, 9_000m, international: 9_000m), 5_000m));
    }

    [Theory]
    [InlineData(5_000.00, true)]
    [InlineData(5_000.01, false)]
    public void Contactless_is_capped_per_purchase_even_without_a_daily_limit(decimal amount, bool allowed)
    {
        var controls = NewCard().Controls!;
        controls.Update(On, Off, On, On, Off, creditLimit: 100_000m);

        var reason = controls.GetLimitDeclineReason(TransactionChannel.Contactless, false, amount, DailySpend.None, 5_000m);

        Assert.Equal(allowed ? null : DeclineReasons.ContactlessLimitExceeded, reason);
    }

    [Fact]
    public void Invalid_limit_changes_nothing()
    {
        var controls = NewCard(10_000m).Controls!;

        // The ATM limit is above the credit limit: Online must NOT have been switched on.
        var ex = Assert.Throws<DomainException>(() =>
            controls.Update(On, On, Off, new ChannelSetting(true, 20_000m), Off, creditLimit: 10_000m));
        Assert.Contains("ATM withdrawals", ex.Message);
        Assert.False(controls.OnlineEnabled);

        Assert.Throws<DomainException>(() => controls.Update(new ChannelSetting(true, 0m), Off, Off, On, Off, 10_000m));
    }

    [Theory]
    [InlineData("2026-10-10T20:00:00", "2026-10-10T18:30:00")]   // 01:30 on 11 Oct in India
    [InlineData("2026-10-10T10:00:00", "2026-10-09T18:30:00")]   // 15:30 on 10 Oct in India
    public void Business_day_starts_at_midnight_ist(string nowUtc, string expectedStartUtc)
    {
        var start = CardControlRules.StartOfBusinessDayUtc(DateTime.Parse(nowUtc), new TimeSpan(5, 30, 0));
        Assert.Equal(DateTime.Parse(expectedStartUtc), start);
        Assert.Equal(DateTimeKind.Utc, start.Kind);
    }

    [Fact]
    public void Atm_cash_is_not_a_purchase_no_emi_no_refund()
    {
        var atm = new SwipeOrigin(TransactionChannel.Atm, "IN", false);
        var cash = CardTransaction.ApprovedSwipe(1, "ATM Andheri", MerchantCategoryCodes.CashWithdrawal, 5_000m, atm);

        Assert.Equal("Cash withdrawals cannot be converted to EMI.",
                     cash.GetEmiIneligibilityReason(100m, 30, DateTime.UtcNow));
        Assert.Throws<DomainException>(() => cash.Refund());
    }

    [Fact]
    public void Refund_keeps_the_channel_and_country_of_the_purchase()
    {
        var purchase = CardTransaction.ApprovedSwipe(1, "Amazon US", "5732", 2_000m,
                                                     new SwipeOrigin(TransactionChannel.Online, "US", true));
        var refund = purchase.Refund();

        Assert.Equal(TransactionChannel.Online, refund.Channel);
        Assert.Equal("US", refund.MerchantCountry);
        Assert.True(refund.IsInternational);
    }
}
