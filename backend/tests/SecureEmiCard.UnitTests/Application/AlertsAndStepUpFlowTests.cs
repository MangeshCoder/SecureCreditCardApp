using Microsoft.EntityFrameworkCore;
using SecureEmiCard.Application.Abstractions.Security;
using SecureEmiCard.Application.Common.Exceptions;
using SecureEmiCard.Application.Features.CardControls;
using SecureEmiCard.Application.Features.Cards;
using SecureEmiCard.Application.Features.Transactions;
using SecureEmiCard.Domain.Common;
using SecureEmiCard.Domain.Entities;
using SecureEmiCard.Domain.Enums;
using SecureEmiCard.Infrastructure.Persistence;

namespace SecureEmiCard.UnitTests.Application;

/// <summary>Module 7 in the real flows: which actions ask for a code, and the alerts every change produces.</summary>
public class AlertsAndStepUpFlowTests
{
    private sealed class FakeCurrentUser : ICurrentUser
    {
        public int UserId { get; set; }
        public bool IsAdmin { get; set; }
    }

    private static readonly ChannelSettingRequest On = new(true, null);
    private static readonly ChannelSettingRequest Off = new(false, null);

    private readonly AppDbContext _db;
    private readonly FakeCurrentUser _user = new();
    private readonly TestServices _services;
    private readonly CardService _cards;
    private readonly TransactionService _transactions;
    private readonly CardControlService _controls;
    private readonly int _aliceId;

    public AlertsAndStepUpFlowTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var alice = new Cardholder("Alice", "A", "alice@test.com", "+911234567", "hash");
        _db.Cardholders.Add(alice);
        _db.SaveChanges();
        _aliceId = alice.CardholderId;

        _services = new TestServices(_db, _user);
        _cards = _services.Cards();
        _transactions = _services.Transactions();
        _controls = _services.CardControls();
    }

    private TestOtp Phone => _services.Otp;

    private async Task<IssuedCardResponse> IssueToAlice(decimal limit = 100_000m, bool online = false)
    {
        _user.IsAdmin = true;
        var issued = await _cards.IssueCardAsync(new IssueCardRequest(_aliceId, limit));
        _user.IsAdmin = false;
        _user.UserId = _aliceId;
        if (online)
            await Phone.CompleteAsync(() => _controls.UpdateAsync(issued.Card.CardId, new UpdateCardControlsRequest(On, On, Off, On, Off)));
        return issued;
    }

    private static SwipeRequest Swipe(IssuedCardResponse c, decimal amount, TransactionChannel channel = TransactionChannel.Pos,
                                      string merchant = "Amazon India") =>
        new(c.CardNumber, c.Card.ExpiryDate.Month, c.Card.ExpiryDate.Year, c.Cvv, c.InitialPin, merchant,
            MerchantCategoryCodes.Electronics, amount, channel);

    private Task<List<Notification>> AlertsAsync() => _db.Notifications.OrderBy(n => n.NotificationId).ToListAsync();

    // ---- one-time codes on actions ---------------------------------------------------------------

    [Fact]
    public async Task Online_payment_needs_a_code_bound_to_amount_and_merchant()
    {
        var card = await IssueToAlice(online: true);

        var required = await Assert.ThrowsAsync<OtpRequiredException>(() => _transactions.SwipeAsync(Swipe(card, 500m, TransactionChannel.Online)));
        Assert.Contains("₹500.00 at Amazon India", required.Challenge.Description);
        Assert.Contains("₹500.00 at Amazon India", Phone.OtpMessages.Last());

        // The code for ₹500 cannot approve ₹5,000 - not even at the same shop.
        Phone.Current = new OtpProof(required.Challenge.ChallengeId, Phone.LastCode);
        await Assert.ThrowsAsync<OtpFailedException>(() => _transactions.SwipeAsync(Swipe(card, 5_000m, TransactionChannel.Online)));
        var approved = await _transactions.SwipeAsync(Swipe(card, 500m, TransactionChannel.Online));
        Phone.Current = null;

        Assert.True(approved.Approved);
        Assert.Equal(TransactionStatus.Completed, (await _db.Transactions.SingleAsync(t => t.TransactionId == approved.TransactionId)).TransactionStatus);
    }

    [Fact]
    public async Task No_sms_for_a_payment_that_is_declined_anyway()
    {
        var card = await IssueToAlice(limit: 1_000m);
        var sentBefore = Phone.OtpMessages.Count();

        var disabled = await _transactions.SwipeAsync(Swipe(card, 100m, TransactionChannel.Online)); // online is off
        Assert.Equal(DeclineReasons.ChannelDisabled(TransactionChannel.Online), disabled.DeclineReason);

        await Phone.CompleteAsync(() => _controls.UpdateAsync(card.Card.CardId, new UpdateCardControlsRequest(On, On, Off, On, Off)));
        sentBefore = Phone.OtpMessages.Count();
        var tooMuch = await _transactions.SwipeAsync(Swipe(card, 5_000m, TransactionChannel.Online));
        Assert.Equal(DeclineReasons.InsufficientCredit, tooMuch.DeclineReason);
        Assert.Equal(sentBefore, Phone.OtpMessages.Count());
    }

    [Fact]
    public async Task Partner_banks_do_3ds_themselves_so_the_gateway_asks_no_code()
    {
        var card = await IssueToAlice(online: true);
        var result = await _transactions.AuthorizeFromGatewayAsync(Swipe(card, 700m, TransactionChannel.Online), "partner-signature");
        Assert.True(result.Approved);
    }

    [Fact]
    public async Task Safer_changes_need_no_code_riskier_ones_do()
    {
        var card = await IssueToAlice();
        var id = card.Card.CardId;

        await _controls.UpdateAsync(id, new UpdateCardControlsRequest(On, Off, Off, Off, Off));                  // ATM off
        await _controls.UpdateAsync(id, new UpdateCardControlsRequest(new(true, 2_000m), Off, Off, Off, Off));    // add a limit
        await _controls.LockAsync(id);
        Assert.Empty(Phone.OtpMessages);

        await Assert.ThrowsAsync<OtpRequiredException>(() => _controls.UnlockAsync(id));
        await Assert.ThrowsAsync<OtpRequiredException>(() =>
            _controls.UpdateAsync(id, new UpdateCardControlsRequest(new(true, 9_000m), Off, Off, Off, Off)));    // raise it
    }

    [Fact]
    public async Task Wrong_pin_is_rejected_before_any_code_is_sent()
    {
        var card = await IssueToAlice();
        var wrong = card.InitialPin == "2468" ? "1357" : "2468";

        await Assert.ThrowsAsync<UnauthorizedException>(() => _cards.ChangePinAsync(card.Card.CardId, new ChangePinRequest(wrong, "2580")));
        await Assert.ThrowsAsync<UnauthorizedException>(() => _cards.RevealCardNumberAsync(card.Card.CardId, new RevealCardNumberRequest(wrong)));
        Assert.Empty(Phone.OtpMessages);
    }

    // ---- alerts ----------------------------------------------------------------------------------

    [Fact]
    public async Task Every_money_movement_creates_an_alert_without_secrets()
    {
        var card = await IssueToAlice();
        var purchase = await _transactions.SwipeAsync(Swipe(card, 1_500m, merchant: "Croma"));
        await _transactions.SwipeAsync(Swipe(card, 200_000m, merchant: "Croma"));                // declined
        await _transactions.LoadAsync(new LoadRequest(card.Card.CardId, 500m));
        _user.IsAdmin = true;
        await _transactions.RefundAsync(purchase.TransactionId!.Value);
        _user.IsAdmin = false;

        var alerts = await AlertsAsync();
        Assert.Equal(new[] { "Your new card is ready", "₹1,500.00 spent at Croma", "Payment declined", "Payment received", "Refund credited" },
                     alerts.Select(a => a.Title));
        Assert.All(alerts, a => Assert.Equal(_aliceId, a.CardholderId));
        Assert.All(alerts, a => Assert.Equal(card.Card.CardId, a.CardId));
        Assert.All(alerts, a => Assert.Equal(NotificationDeliveryStatus.Pending, a.DeliveryStatus));
        Assert.Contains("card ending " + card.CardNumber[^4..], alerts[1].Message);
        Assert.Contains("Insufficient credit", alerts[2].Message);
        Assert.All(alerts, a =>
        {
            Assert.DoesNotContain(card.CardNumber, a.Message);
            Assert.DoesNotContain($" {card.Cvv} ", $" {a.Message} ");
        });
    }

    [Fact]
    public async Task Security_changes_are_alerted_too()
    {
        var card = await IssueToAlice();
        var id = card.Card.CardId;

        await _controls.LockAsync(id);
        await Phone.ApproveAsync(() => _controls.UnlockAsync(id));
        await Phone.ApproveAsync(() => _cards.ChangePinAsync(id, new ChangePinRequest(card.InitialPin, "2580")));
        await Phone.ApproveAsync(() => _cards.RevealCardNumberAsync(id, new RevealCardNumberRequest("2580")));

        Assert.Equal(new[] { "Your new card is ready", "Card locked", "Card unlocked", "PIN changed", "Card number viewed" },
                     (await AlertsAsync()).Select(a => a.Title));
    }

    [Fact]
    public async Task A_change_that_fails_leaves_no_alert()
    {
        var card = await IssueToAlice();
        var declined = await _transactions.SwipeAsync(Swipe(card, 200_000m));
        var before = (await AlertsAsync()).Count;

        _user.IsAdmin = true;
        await Assert.ThrowsAsync<DomainException>(() => _transactions.RefundAsync(declined.TransactionId!.Value));

        Assert.Equal(before, (await AlertsAsync()).Count);                                       // no "Refund credited"
    }
}
