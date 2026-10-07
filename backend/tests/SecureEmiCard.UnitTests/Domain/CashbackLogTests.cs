using SecureEmiCard.Domain.Common;
using SecureEmiCard.Domain.Entities;
using SecureEmiCard.Domain.Enums;

namespace SecureEmiCard.UnitTests.Domain;

public class CashbackLogTests
{
    [Fact]
    public void Only_approved_swipes_earn_and_reversal_is_the_negative_amount()
    {
        var swipe = CardTransaction.ApprovedSwipe(1, "Shop", "5411", 1_000m);
        var earned = CashbackLog.Earned(swipe, 3m, 30m);
        var reversal = earned.CreateReversal();

        Assert.Equal(-30m, reversal.CashbackAmount);
        Assert.Equal(CashbackType.Reversed, reversal.CashbackType);
        Assert.Throws<DomainException>(() => reversal.CreateReversal());

        Assert.Throws<DomainException>(() =>
            CashbackLog.Earned(CardTransaction.DeclinedSwipe(1, "Shop", "5411", 1_000m, "x"), 3m, 30m));
        Assert.Throws<DomainException>(() => CashbackLog.Earned(CardTransaction.Load(1, 100m), 3m, 3m));
    }

    [Fact]
    public void Card_reward_is_a_statement_credit_that_can_be_taken_back()
    {
        var card = new CreditCard(1, "enc", "hash", "XXXX-XXXX-XXXX-1234", "cvv", "pin", 1_000m,
                                  DateOnly.FromDateTime(DateTime.UtcNow.AddYears(5)));
        card.Debit(500m);
        card.CreditReward(15m);
        Assert.Equal(485m, card.OutstandingAmount);

        card.ReverseReward(15m);
        Assert.Equal(500m, card.OutstandingAmount);
    }
}
