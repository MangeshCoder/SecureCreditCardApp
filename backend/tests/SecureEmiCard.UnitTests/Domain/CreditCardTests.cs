using SecureEmiCard.Domain.Common;
using SecureEmiCard.Domain.Entities;
using SecureEmiCard.Domain.Enums;

namespace SecureEmiCard.UnitTests.Domain;

public class CreditCardTests
{
    private static CreditCard NewCard(decimal limit = 10_000m) =>
        new(1, "enc", "XXXX-XXXX-XXXX-1234", "cvv", "pin", limit, DateOnly.FromDateTime(DateTime.UtcNow.AddYears(5)));

    [Fact]
    public void New_card_is_active_with_full_available_balance()
    {
        var card = NewCard(5_000m);
        Assert.Equal(CardStatus.Active, card.CardStatus);
        Assert.Equal(5_000m, card.AvailableBalance);
        Assert.Equal(0m, card.OutstandingAmount);
    }

    [Fact]
    public void Credit_limit_must_be_positive()
        => Assert.Throws<DomainException>(() => NewCard(0m));

    [Fact]
    public void Block_then_activate_round_trips_and_rejects_duplicates()
    {
        var card = NewCard();
        card.Block();
        Assert.Equal(CardStatus.Blocked, card.CardStatus);
        Assert.Throws<DomainException>(card.Block);

        card.Activate();
        Assert.Equal(CardStatus.Active, card.CardStatus);
        Assert.Throws<DomainException>(card.Activate);
    }

    [Fact]
    public void Updating_limit_keeps_available_balance_in_sync()
    {
        var card = NewCard(10_000m);
        card.UpdateCreditLimit(15_000m);
        Assert.Equal(15_000m, card.CreditLimit);
        Assert.Equal(15_000m, card.AvailableBalance);
    }

    [Fact]
    public void Pin_cannot_be_changed_on_blocked_card()
    {
        var card = NewCard();
        card.Block();
        Assert.Throws<DomainException>(() => card.ChangePin("new-hash"));
    }
}
