using SecureEmiCard.Domain.Common;
using SecureEmiCard.Domain.Entities;
using SecureEmiCard.Domain.Enums;

namespace SecureEmiCard.UnitTests.Domain;

public class CardMoneyRulesTests
{
    private static CreditCard NewCard(decimal limit = 1_000m) =>
        new(1, "enc", "hash", "XXXX-XXXX-XXXX-1234", "cvv", "pin", limit, DateOnly.FromDateTime(DateTime.UtcNow.AddYears(5)));

    [Fact]
    public void Debit_and_credit_move_the_available_balance()
    {
        var card = NewCard(1_000m);
        card.Debit(300m);
        Assert.Equal(700m, card.AvailableBalance);
        Assert.Equal(300m, card.OutstandingAmount);

        card.Credit(100m);
        Assert.Equal(800m, card.AvailableBalance);
    }

    [Fact]
    public void Cannot_spend_more_than_available_or_repay_more_than_outstanding()
    {
        var card = NewCard(1_000m);
        Assert.Equal(DeclineReasons.InsufficientCredit, card.GetSwipeDeclineReason(1_000.01m));
        Assert.Throws<DomainException>(() => card.Debit(1_000.01m));

        card.Debit(200m);
        Assert.Throws<DomainException>(() => card.Credit(200.01m));
    }

    [Fact]
    public void Blocked_card_cannot_be_debited_but_can_be_repaid()
    {
        var card = NewCard();
        card.Debit(100m);
        card.Block();

        Assert.Equal(DeclineReasons.CardBlocked, card.GetSwipeDeclineReason(1m));
        card.Credit(100m);
        Assert.Equal(0m, card.OutstandingAmount);
    }

    [Fact]
    public void Three_wrong_pins_block_the_card_and_unblock_resets_the_counter()
    {
        var card = NewCard();
        card.RegisterFailedPinAttempt();
        card.RegisterFailedPinAttempt();
        Assert.Equal(CardStatus.Active, card.CardStatus);
        Assert.Equal(1, card.RemainingPinAttempts);

        card.RegisterFailedPinAttempt();
        Assert.Equal(CardStatus.Blocked, card.CardStatus);

        card.Activate();
        Assert.Equal(0, card.FailedPinAttempts);
    }

    [Fact]
    public void Refund_only_once_and_only_for_completed_swipes()
    {
        var swipe = CardTransaction.ApprovedSwipe(1, "Shop", "5411", 50m);
        var refund = swipe.Refund();

        Assert.Equal(TransactionStatus.Refunded, swipe.TransactionStatus);
        Assert.Equal(TransactionType.Refund, refund.TransactionType);
        Assert.Equal(50m, refund.Amount);
        Assert.Throws<DomainException>(() => swipe.Refund());

        Assert.Throws<DomainException>(() => CardTransaction.Load(1, 10m).Refund());
        Assert.Throws<DomainException>(() => CardTransaction.DeclinedSwipe(1, "Shop", "5411", 5m, "x").Refund());
    }
}
